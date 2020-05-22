using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTK;
using StorybrewCommon.Storyboarding.Commands;
using StorybrewCommon.Storyboarding.CommandValues;

namespace StorybrewCommon.Storyboarding
{
    public class ComposedOsbSprite : OsbSprite
    {
        private StoryboardLayer StoryboardLayer;
        public int MaxCommandCount = 300;

        public ComposedOsbSprite(StoryboardLayer layer, string texturePath) : this(layer, texturePath, OsbOrigin.Centre, new Vector2(320, 240)) { }

        public ComposedOsbSprite(StoryboardLayer layer, string texturePath, OsbOrigin origin) : this(layer, texturePath, origin, new Vector2(320, 240)) { }

        public ComposedOsbSprite(StoryboardLayer layer, string texturePath, OsbOrigin origin, Vector2 initialPosition)
        {
            StoryboardLayer = layer;
            TexturePath = texturePath;
            Origin = origin;
            InitialPosition = initialPosition;
        }

        public void Fragment()
        {
            HashSet<int> fragmentationTimes = GetFragmentationTimes();

            if (IsFragmentable() && fragmentationTimes.Count > 1 && Commands.Count() > MaxCommandCount)
            {
                List<ICommand> commands = Commands.ToList();

                while (commands.Count > 0)
                {
                    var segment = GetNextSegment(fragmentationTimes, commands);
                    var sprite = StoryboardLayer.CreateSprite(TexturePath, Origin, InitialPosition);
                    foreach (var command in segment)
                    {
                        sprite.AddCommand(command);
                    }
                }
            }
            else
            {
                var sprite = StoryboardLayer.CreateSprite(TexturePath, Origin, InitialPosition);
                foreach (var command in Commands)
                {
                    sprite.AddCommand(command);
                }
            }
        }

        private bool IsFragmentable()
        {
            if (CommandCount < MaxCommandCount)
                return false;

            return !(moveTimeline.HasOverlap ||
                     moveXTimeline.HasOverlap ||
                     moveYTimeline.HasOverlap ||
                     rotateTimeline.HasOverlap ||
                     scaleTimeline.HasOverlap ||
                     scaleVecTimeline.HasOverlap ||
                     fadeTimeline.HasOverlap ||
                     flipHTimeline.HasOverlap ||
                     flipVTimeline.HasOverlap ||
                     colorTimeline.HasOverlap ||
                     additiveTimeline.HasOverlap);
        }

        private HashSet<int> GetFragmentationTimes()
        {
            HashSet<int> fragmentationTimes = new HashSet<int>();
            var nonFragmentableCommands = Commands.Where(c => !c.IsFragmentable()).ToList();

            fragmentationTimes.UnionWith(Enumerable.Range((int)Commands.Min(c => c.StartTime), (int)Commands.Max(c => c.EndTime)));
            nonFragmentableCommands.ForEach(c => fragmentationTimes.RemoveWhere(t => t > c.StartTime && t < c.EndTime));

            return fragmentationTimes;
        }

        private List<ICommand> GetNextSegment(HashSet<int> fragmentationTimes, List<ICommand> commands)
        {
            List<ICommand> segment = new List<ICommand>();

            int startTime = fragmentationTimes.Min();
            int endTime;
            int maxCommandCount = MaxCommandCount;

            //split the last 2 segments evenly so we don't have weird 5 command leftovers
            if (commands.Count < MaxCommandCount * 2 && commands.Count > MaxCommandCount)
                maxCommandCount = (int)Math.Ceiling(commands.Count / 2.0);

            if (commands.Count < maxCommandCount)
                endTime = fragmentationTimes.Max() + 1;
            else
            {
                var cEndTime = (int)commands.OrderBy(c => c.StartTime).ElementAt(maxCommandCount - 1).StartTime;
                if (fragmentationTimes.Contains(cEndTime))
                    endTime = cEndTime;
                else
                    endTime = fragmentationTimes.Where(t => t < cEndTime).Max();
            }

            foreach (var cmd in commands.Where(c => c.StartTime < endTime))
            {
                var sTime = Math.Max(startTime, (int)Math.Round(cmd.StartTime));
                var eTime = Math.Min(endTime, (int)Math.Round(cmd.EndTime));
                ICommand command;
                if (sTime == (int)Math.Round(cmd.StartTime) && eTime == (int)Math.Round(cmd.EndTime))
                {
                    command = cmd;
                }
                else
                {
                    var type = cmd.GetType();
                    var easingProp = type.GetProperty("Easing");
                    var valueAtMethod = type.GetMethod("ValueAtTime");
                    var startValue = valueAtMethod.Invoke(cmd, new object[] { sTime });
                    var endValue = valueAtMethod.Invoke(cmd, new object[] { eTime });
                    var easing = easingProp.GetValue(cmd);

                    if (!(cmd is ParameterCommand))
                        command = (ICommand)Activator.CreateInstance(type, new object[] { easing, sTime, eTime, startValue, endValue });
                    else
                        command = (ICommand)Activator.CreateInstance(type, new object[] { easing, sTime, eTime, startValue });
                }

                segment.Add(command);
            }

            AddStaticCommands(segment, startTime);

            fragmentationTimes.RemoveWhere(t => t < endTime);
            commands.RemoveAll(c => c.EndTime <= endTime);

            return segment;
        }

        private void AddStaticCommands(List<ICommand> segment, int startTime)
        {
            if (moveTimeline.HasCommands && !segment.Any(c => c is MoveCommand && c.StartTime == startTime))
            {
                var value = moveTimeline.ValueAtTime(startTime);
                segment.Add(new MoveCommand(OsbEasing.None, startTime, startTime, value, value));
            }

            if (moveXTimeline.HasCommands && !segment.Any(c => c is MoveXCommand && c.StartTime == startTime))
            {
                var value = moveXTimeline.ValueAtTime(startTime);
                segment.Add(new MoveXCommand(OsbEasing.None, startTime, startTime, value, value));
            }

            if (moveYTimeline.HasCommands && !segment.Any(c => c is MoveYCommand && c.StartTime == startTime))
            {
                var value = moveYTimeline.ValueAtTime(startTime);
                segment.Add(new MoveYCommand(OsbEasing.None, startTime, startTime, value, value));
            }

            if (rotateTimeline.HasCommands && !segment.Any(c => c is RotateCommand && c.StartTime == startTime))
            {
                var value = rotateTimeline.ValueAtTime(startTime);
                segment.Add(new RotateCommand(OsbEasing.None, startTime, startTime, value, value));
            }

            if (scaleTimeline.HasCommands && !segment.Any(c => c is ScaleCommand && c.StartTime == startTime))
            {
                var value = scaleTimeline.ValueAtTime(startTime);
                segment.Add(new ScaleCommand(OsbEasing.None, startTime, startTime, value, value));
            }

            if (scaleVecTimeline.HasCommands && !segment.Any(c => c is VScaleCommand && c.StartTime == startTime))
            {
                var value = scaleVecTimeline.ValueAtTime(startTime);
                segment.Add(new VScaleCommand(OsbEasing.None, startTime, startTime, value, value));
            }

            if (colorTimeline.HasCommands && !segment.Any(c => c is ColorCommand && c.StartTime == startTime))
            {
                var value = colorTimeline.ValueAtTime(startTime);
                segment.Add(new ColorCommand(OsbEasing.None, startTime, startTime, value, value));
            }

            if (fadeTimeline.HasCommands && !segment.Any(c => c is FadeCommand && c.StartTime == startTime))
            {
                var value = fadeTimeline.ValueAtTime(startTime);
                segment.Add(new FadeCommand(OsbEasing.None, startTime, startTime, value, value));
            }
        }
    }

    static class OsbSpriteExtensions
    {
        public static void AddCommand(this OsbSprite sprite, ICommand command)
        {
            if (command is ColorCommand colorCommand)
                sprite.Color(colorCommand.Easing, colorCommand.StartTime, colorCommand.EndTime, colorCommand.StartValue, colorCommand.EndValue);
            else if (command is FadeCommand fadeCommand)
                sprite.Fade(fadeCommand.Easing, fadeCommand.StartTime, fadeCommand.EndTime, fadeCommand.StartValue, fadeCommand.EndValue);
            else if (command is ScaleCommand scaleCommand)
                sprite.Scale(scaleCommand.Easing, scaleCommand.StartTime, scaleCommand.EndTime, scaleCommand.StartValue, scaleCommand.EndValue);
            else if (command is VScaleCommand vScaleCommand)
                sprite.ScaleVec(vScaleCommand.Easing, vScaleCommand.StartTime, vScaleCommand.EndTime, vScaleCommand.StartValue, vScaleCommand.EndValue);
            else if (command is ParameterCommand parameterCommand)
                sprite.Parameter(parameterCommand.Easing, parameterCommand.StartTime, parameterCommand.EndTime, parameterCommand.StartValue);
            else if (command is MoveCommand moveCommand)
                sprite.Move(moveCommand.Easing, moveCommand.StartTime, moveCommand.EndTime, moveCommand.StartValue, moveCommand.EndValue);
            else if (command is MoveXCommand moveXCommand)
                sprite.MoveX(moveXCommand.Easing, moveXCommand.StartTime, moveXCommand.EndTime, moveXCommand.StartValue, moveXCommand.EndValue);
            else if (command is MoveYCommand moveYCommand)
                sprite.MoveY(moveYCommand.Easing, moveYCommand.StartTime, moveYCommand.EndTime, moveYCommand.StartValue, moveYCommand.EndValue);
            else if (command is RotateCommand rotateCommand)
                sprite.Rotate(rotateCommand.Easing, rotateCommand.StartTime, rotateCommand.EndTime, rotateCommand.StartValue, rotateCommand.EndValue);
            else if (command is LoopCommand loopCommand)
            {
                sprite.StartLoopGroup(loopCommand.StartTime, loopCommand.LoopCount);
                foreach (var cmd in loopCommand.Commands)
                    AddCommand(sprite, cmd);
                sprite.EndGroup();
            }
            else if (command is TriggerCommand triggerCommand)
            {
                sprite.StartTriggerGroup(triggerCommand.TriggerName, triggerCommand.StartTime, triggerCommand.EndTime, triggerCommand.Group);
                foreach (var cmd in triggerCommand.Commands)
                    AddCommand(sprite, cmd);
                sprite.EndGroup();
            }
        }

        public static bool IsFragmentable(this ICommand command)
        {
            if (command is ParameterCommand)
                return true;

            if (command.StartTime == command.EndTime)
                return true;

            var type = command.GetType();
            var easingProp = type.GetProperty("Easing");
            var easing = easingProp.GetValue(command);
            return (OsbEasing)easing == OsbEasing.None;
        }
    }
}
