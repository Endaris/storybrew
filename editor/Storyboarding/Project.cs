﻿using OpenTK;
using StorybrewCommon.Scripting;
using StorybrewCommon.Storyboarding;
using StorybrewCommon.Util;
using StorybrewEditor.Graphics;
using StorybrewEditor.Graphics.Cameras;
using StorybrewEditor.Graphics.Textures;
using StorybrewEditor.Mapset;
using StorybrewEditor.Scripting;
using StorybrewEditor.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace StorybrewEditor.Storyboarding
{
    public class Project : IDisposable
    {
        public const string Extension = ".sbp";
        public const string DefaultFilename = "project" + Extension;
        public const string ProjectsFolder = "projects";

        public const string FileFilter = "project files (*" + Extension + ")|*" + Extension;

        private string projectPath;
        private ScriptManager<StoryboardObjectGenerator> scriptManager;

        private string commonScriptsSourcePath;
        public string CommonScriptsPath => commonScriptsSourcePath;

        private string scriptsSourcePath;
        public string ScriptsPath => scriptsSourcePath;

        private TextureContainer textureContainer;
        public TextureContainer TextureContainer => textureContainer;

        public string AudioPath
        {
            get
            {
                checkMapsetPath();
                foreach (var beatmap in mapsetManager.Beatmaps)
                {
                    var path = Path.Combine(MapsetPath, beatmap.AudioFilename);
                    if (!File.Exists(path)) continue;
                    return path;
                }

                foreach (var mp3Path in Directory.GetFiles(MapsetPath, "*.mp3", SearchOption.TopDirectoryOnly))
                    return mp3Path;

                return null;
            }
        }

        private LayerManager layerManager = new LayerManager();
        public LayerManager LayerManager => layerManager;

        public Project(string projectPath, bool withCommonScripts)
        {
            this.projectPath = projectPath;

            reloadTextures();

            scriptsSourcePath = Path.GetDirectoryName(projectPath);
            if (withCommonScripts)
            {
                commonScriptsSourcePath = Path.GetFullPath(Path.Combine("..", "..", "..", "scripts"));
                if (!Directory.Exists(commonScriptsSourcePath))
                {
                    commonScriptsSourcePath = Path.GetFullPath("scripts");
                    if (!Directory.Exists(commonScriptsSourcePath))
                        Directory.CreateDirectory(commonScriptsSourcePath);
                }
            }
            Trace.WriteLine($"Scripts path - project:{scriptsSourcePath}, common:{commonScriptsSourcePath}");

            var compiledScriptsPath = Path.GetFullPath("cache/scripts");
            if (!Directory.Exists(compiledScriptsPath))
                Directory.CreateDirectory(compiledScriptsPath);
            else
            {
                cleanupFolder(compiledScriptsPath, "*.dll");
#if DEBUG
                cleanupFolder(compiledScriptsPath, "*.pdb");
#endif
            }
            var referencedAssemblies = new string[]
            {
                "System.dll",
                "OpenTK.dll",
                Assembly.GetAssembly(typeof(Script)).Location,
            };
            scriptManager = new ScriptManager<StoryboardObjectGenerator>("StorybrewScripts", scriptsSourcePath, commonScriptsSourcePath, compiledScriptsPath, referencedAssemblies);
            effectUpdateQueue.OnActionFailed += (effect, e) => Trace.WriteLine($"Action failed for '{effect}': {e.Message}");

            OnMainBeatmapChanged += (sender, e) =>
            {
                foreach (var effect in effects)
                    QueueEffectUpdate(effect);
            };
        }

        #region Display

        public static readonly OsbLayer[] OsbLayers = new OsbLayer[] { OsbLayer.Background, OsbLayer.Fail, OsbLayer.Pass, OsbLayer.Foreground, };

        public double DisplayTime;

        public void Draw(DrawContext drawContext, Camera camera, Box2 bounds, float opacity)
        {
            effectUpdateQueue.Enabled = true;
            layerManager.Draw(drawContext, camera, bounds, opacity);
        }

        private void reloadTextures()
        {
            textureContainer?.Dispose();
            textureContainer = new TextureContainerSeparate(false);
        }

        #endregion

        #region Effects

        private List<Effect> effects = new List<Effect>();
        public IEnumerable<Effect> Effects => effects;
        public event EventHandler OnEffectsChanged;

        public EffectStatus effectsStatus = EffectStatus.Initializing;
        public EffectStatus EffectsStatus => effectsStatus;
        public event EventHandler OnEffectsStatusChanged;

        private AsyncActionQueue<Effect> effectUpdateQueue = new AsyncActionQueue<Effect>("Effect Updates", false);
        public void QueueEffectUpdate(Effect effect)
            => effectUpdateQueue.Queue(effect, effect.Path, (e) => e.Update());

        public IEnumerable<string> GetEffectNames()
            => scriptManager.GetScriptNames();

        public Effect GetEffectByName(string name)
            => effects.Find(e => e.Name == name);

        public Effect AddEffect(string effectName)
        {
            if (disposedValue) throw new ObjectDisposedException(nameof(Project));

            var effect = new ScriptedEffect(this, scriptManager.Get(effectName));

            effects.Add(effect);
            effect.OnChanged += Effect_OnChanged;
            refreshEffectsStatus();

            OnEffectsChanged?.Invoke(this, EventArgs.Empty);
            QueueEffectUpdate(effect);
            return effect;
        }

        public void Remove(Effect effect)
        {
            if (disposedValue) throw new ObjectDisposedException(nameof(Project));

            effect.Clear();
            effects.Remove(effect);
            effect.OnChanged -= Effect_OnChanged;
            refreshEffectsStatus();

            OnEffectsChanged?.Invoke(this, EventArgs.Empty);
        }

        public string GetUniqueEffectName()
        {
            var count = 1;
            string name;
            do
                name = $"Effect {count++}";
            while (GetEffectByName(name) != null);
            return name;
        }

        private void Effect_OnChanged(object sender, EventArgs e)
            => refreshEffectsStatus();

        private void refreshEffectsStatus()
        {
            var previousStatus = effectsStatus;
            var isUpdating = false;
            var hasError = false;

            foreach (var effect in effects)
            {
                switch (effect.Status)
                {
                    case EffectStatus.Loading:
                    case EffectStatus.Configuring:
                    case EffectStatus.Updating:
                    case EffectStatus.ReloadPending:
                        isUpdating = true;
                        break;

                    case EffectStatus.CompilationFailed:
                    case EffectStatus.LoadingFailed:
                    case EffectStatus.ExecutionFailed:
                        hasError = true;
                        break;

                    case EffectStatus.Initializing:
                    case EffectStatus.Ready:
                        break;
                }
            }
            effectsStatus = hasError ? EffectStatus.ExecutionFailed :
                isUpdating ? EffectStatus.Updating : EffectStatus.Ready;
            if (effectsStatus != previousStatus)
                OnEffectsStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Mapset

        private string mapsetPath;
        public string MapsetPath
        {
            get { return mapsetPath; }
            set
            {
                if (mapsetPath == value) return;
                mapsetPath = value;
                refreshMapset();
            }
        }

        private MapsetManager mapsetManager;
        public MapsetManager MapsetManager => mapsetManager;

        private EditorBeatmap mainBeatmap;
        public EditorBeatmap MainBeatmap
        {
            get
            {
                if (mainBeatmap == null)
                    SwitchMainBeatmap();

                return mainBeatmap;
            }
            set
            {
                if (mainBeatmap == value) return;
                mainBeatmap = value;
                OnMainBeatmapChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler OnMainBeatmapChanged;

        public void SwitchMainBeatmap()
        {
            var takeNextBeatmap = false;
            foreach (var beatmap in mapsetManager.Beatmaps)
            {
                if (takeNextBeatmap)
                {
                    MainBeatmap = beatmap;
                    return;
                }
                else if (beatmap == mainBeatmap)
                    takeNextBeatmap = true;
            }
            foreach (var beatmap in mapsetManager.Beatmaps)
            {
                MainBeatmap = beatmap;
                return;
            }
        }

        private void refreshMapset()
        {
            mainBeatmap = null;
            mapsetManager?.Dispose();
            mapsetManager = new MapsetManager(mapsetPath);
            mapsetManager.OnFileChanged += mapsetManager_OnFileChanged;
        }

        private void mapsetManager_OnFileChanged(object sender, FileSystemEventArgs e)
        {
            var extension = Path.GetExtension(e.Name);
            if (extension == ".png" || extension == ".jpg" || extension == ".jpeg")
                reloadTextures();
        }

        #endregion

        #region Save / Load / Export

        public const int Version = 3;

        public void Save()
        {
            if (disposedValue) throw new ObjectDisposedException(nameof(Project));

            using (var stream = new SafeWriteStream(projectPath))
            using (var w = new BinaryWriter(stream, Encoding.UTF8))
            {
                w.Write(Version);
                w.Write(Program.FullName);

                w.Write(MapsetPath);
                w.Write(MainBeatmap.Id);
                w.Write(MainBeatmap.Name);

                w.Write(effects.Count);
                foreach (var effect in effects)
                {
                    w.Write(effect.BaseName);
                    w.Write(effect.Name);

                    var config = effect.Config;
                    w.Write(config.FieldCount);
                    foreach (var field in config.SortedFields)
                    {
                        w.Write(field.Name);
                        w.Write(field.DisplayName);
                        ObjectSerializer.Write(w, field.Value);

                        w.Write(field.AllowedValues?.Length ?? 0);
                        if (field.AllowedValues != null)
                            foreach (var allowedValue in field.AllowedValues)
                            {
                                w.Write(allowedValue.Name);
                                ObjectSerializer.Write(w, allowedValue.Value);
                            }
                    }
                }

                w.Write(layerManager.LayersCount);
                foreach (var layer in layerManager.Layers)
                {
                    w.Write(layer.Identifier);
                    w.Write(effects.IndexOf(layer.Effect));
                    w.Write(layer.DiffSpecific);
                    w.Write((int)layer.OsbLayer);
                    w.Write(layer.Visible);
                }
                stream.Commit();
            }
        }

        public static Project Load(string projectPath, bool withCommonScripts)
        {
            var project = new Project(projectPath, withCommonScripts);
            using (var stream = new FileStream(projectPath, FileMode.Open))
            using (var r = new BinaryReader(stream, Encoding.UTF8))
            {
                var version = r.ReadInt32();
                if (version > Version)
                    throw new InvalidOperationException("This project was saved with a more recent version, you need to update to open it");

                var savedBy = r.ReadString();
                Debug.Print($"Loading project saved by {savedBy}");

                project.MapsetPath = r.ReadString();
                if (version >= 1)
                {
                    var mainBeatmapId = r.ReadInt64();
                    var mainBeatmapName = r.ReadString();

                    foreach (var beatmap in project.MapsetManager.Beatmaps)
                        if ((mainBeatmapId > 0 && beatmap.Id == mainBeatmapId) ||
                            (mainBeatmapName.Length > 0 && beatmap.Name == mainBeatmapName))
                        {
                            project.MainBeatmap = beatmap;
                            break;
                        }
                }

                var effectCount = r.ReadInt32();
                for (int effectIndex = 0; effectIndex < effectCount; effectIndex++)
                {
                    var baseName = r.ReadString();
                    var name = r.ReadString();

                    var effect = project.AddEffect(baseName);
                    effect.Name = name;

                    if (version >= 1)
                    {
                        var fieldCount = r.ReadInt32();
                        for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
                        {
                            var fieldName = r.ReadString();
                            var fieldDisplayName = r.ReadString();
                            var fieldValue = ObjectSerializer.Read(r);

                            var allowedValueCount = r.ReadInt32();
                            var allowedValues = allowedValueCount > 0 ? new NamedValue[allowedValueCount] : null;
                            for (int allowedValueIndex = 0; allowedValueIndex < allowedValueCount; allowedValueIndex++)
                            {
                                var allowedValueName = r.ReadString();
                                var allowedValue = ObjectSerializer.Read(r);
                                allowedValues[allowedValueIndex] = new NamedValue()
                                {
                                    Name = allowedValueName,
                                    Value = allowedValue,
                                };
                            }
                            effect.Config.UpdateField(fieldName, fieldDisplayName, fieldIndex, fieldValue.GetType(), fieldValue, allowedValues);
                        }
                    }
                }

                var layerCount = r.ReadInt32();
                for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
                {
                    var identifier = r.ReadString();
                    var effectIndex = r.ReadInt32();
                    var diffSpecific = version >= 3 ? r.ReadBoolean() : false;
                    var osbLayer = version >= 2 ? (OsbLayer)r.ReadInt32() : OsbLayer.Background;
                    var visible = r.ReadBoolean();

                    var effect = project.effects[effectIndex];
                    effect.AddPlaceholder(new EditorStoryboardLayer(identifier, effect)
                    {
                        DiffSpecific = diffSpecific,
                        OsbLayer = osbLayer,
                        Visible = visible,
                    });
                }
            }
            return project;
        }

        public static Project Create(string projectFolderName, string mapsetPath, bool withCommonScripts)
        {
            if (!Directory.Exists(ProjectsFolder))
                Directory.CreateDirectory(ProjectsFolder);

            var hasInvalidCharacters = false;
            foreach (var character in Path.GetInvalidFileNameChars())
                if (projectFolderName.Contains(character.ToString()))
                {
                    hasInvalidCharacters = true;
                    break;
                }

            if (hasInvalidCharacters || string.IsNullOrWhiteSpace(projectFolderName))
                throw new InvalidOperationException($"'{projectFolderName}' isn't a valid project folder name");

            var projectFolderPath = Path.Combine(ProjectsFolder, projectFolderName);
            if (Directory.Exists(projectFolderPath))
                throw new InvalidOperationException($"A project already exists at '{projectFolderPath}'");

            Directory.CreateDirectory(projectFolderPath);
            using (var stream = new MemoryStream(Resources.projecttemplate))
            using (var zip = new ZipArchive(stream))
                zip.ExtractToDirectory(projectFolderPath);

            var project = new Project(Path.Combine(projectFolderPath, DefaultFilename), withCommonScripts)
            {
                MapsetPath = mapsetPath,
            };
            project.Save();

            return project;
        }

        public static string Migrate(string projectPath, string projectFolderName)
        {
            Trace.WriteLine($"Migrating project '{projectPath}' to '{projectFolderName}'");

            using (var project = Load(projectPath, false))
            using (var placeholderProject = Create(projectFolderName, project.MapsetPath, false))
            {
                var oldProjectPath = project.projectPath;

                project.projectPath = placeholderProject.projectPath;
                project.Save();

                File.Move(oldProjectPath, project.projectPath + ".bak");
                return project.projectPath;
            }
        }

        /// <summary>
        /// Doesn't run in the main thread
        /// </summary>
        public void ExportToOsb()
        {
            if (disposedValue) throw new ObjectDisposedException(nameof(Project));

            string osbPath = null;
            List<EditorStoryboardLayer> localLayers = null;
            Program.RunMainThread(() =>
            {
                osbPath = getOsbPath();
                localLayers = new List<EditorStoryboardLayer>(layerManager.Layers);
            });

            Debug.Print($"Exporting osb to {osbPath}");
            var exportSettings = new ExportSettings();

            using (var stream = new SafeWriteStream(osbPath))
            using (var writer = new StreamWriter(stream, Encoding.UTF8))
            {
                writer.WriteLine("[Events]");
                writer.WriteLine("//Background and Video events");
                foreach (var osbLayer in OsbLayers)
                {
                    writer.WriteLine($"//Storyboard Layer {(int)osbLayer} ({osbLayer})");
                    foreach (var layer in localLayers)
                        if (layer.OsbLayer == osbLayer)
                            layer.WriteOsbSprites(writer, exportSettings);
                }
                writer.WriteLine("//Storyboard Sound Samples");
                stream.Commit();
            }
        }

        private string getOsbPath()
        {
            checkMapsetPath();

            // Find the correct osb filename from .osu files
            var regex = new Regex(@"^(.+ - .+ \(.+\)) \[.+\].osu$");
            foreach (var osuFilePath in Directory.GetFiles(MapsetPath, "*.osu", SearchOption.TopDirectoryOnly))
            {
                var osuFilename = Path.GetFileName(osuFilePath);

                Match match;
                if ((match = regex.Match(osuFilename)).Success)
                    return Path.Combine(MapsetPath, $"{match.Groups[1].Value}.osb");
            }

            // Use an existing osb
            foreach (var osbFilePath in Directory.GetFiles(MapsetPath, "*.osb", SearchOption.TopDirectoryOnly))
                return osbFilePath;

            // Whatever
            return Path.Combine(MapsetPath, "storyboard.osb");
        }

        private void checkMapsetPath()
        {
            if (!Directory.Exists(MapsetPath)) throw new InvalidOperationException($"Mapset directory doesn't exist.\n{MapsetPath}");
        }

        private static void cleanupFolder(string path, string searchPattern)
        {
            foreach (var filename in Directory.GetFiles(path, searchPattern, SearchOption.TopDirectoryOnly))
                try
                {
                    File.Delete(filename);
                    Debug.Print($"{filename} deleted");
                }
                catch (Exception e)
                {
                    Trace.WriteLine($"{filename} couldn't be deleted: {e.Message}");
                }
        }

        #endregion

        #region IDisposable Support

        public bool IsDisposed => disposedValue;
        private bool disposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    mapsetManager?.Dispose();
                    effectUpdateQueue.Dispose();
                    scriptManager.Dispose();
                    textureContainer.Dispose();
                }
                mapsetManager = null;
                effectUpdateQueue = null;
                scriptManager = null;
                textureContainer = null;
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        #endregion
    }
}
