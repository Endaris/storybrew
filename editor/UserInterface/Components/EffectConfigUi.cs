﻿using BrewLib.UserInterface;
using BrewLib.Util;
using OpenTK;
using OpenTK.Graphics;
using StorybrewCommon.Storyboarding;
using StorybrewCommon.Util;
using StorybrewEditor.Storyboarding;
using System;
using System.Globalization;

namespace StorybrewEditor.UserInterface.Components
{
    public class EffectConfigUi : Widget
    {
        private Label titleLabel;
        private LinearLayout layout;
        private LinearLayout configFieldsLayout;

        public override Vector2 MinSize => layout.MinSize;
        public override Vector2 MaxSize => layout.MaxSize;
        public override Vector2 PreferredSize => layout.PreferredSize;

        private Effect effect;
        public Effect Effect
        {
            get { return effect; }
            set
            {
                if (effect == value) return;

                if (effect != null)
                {
                    effect.OnChanged -= Effect_OnChanged;
                    effect.OnConfigFieldsChanged -= Effect_OnConfigFieldsChanged;
                }
                effect = value;
                if (effect != null)
                {
                    effect.OnChanged += Effect_OnChanged;
                    effect.OnConfigFieldsChanged += Effect_OnConfigFieldsChanged;
                }

                updateEffect();
                updateFields();
            }
        }

        public EffectConfigUi(WidgetManager manager) : base(manager)
        {
            Button closeButton;

            Add(layout = new LinearLayout(manager)
            {
                StyleName = "panel",
                Padding = new FourSide(16),
                FitChildren = true,
                Fill = true,
                Children = new Widget[]
                {
                    new LinearLayout(manager)
                    {
                        Fill = true,
                        FitChildren = true,
                        Horizontal = true,
                        CanGrow = false,
                        Children = new Widget[]
                        {
                            titleLabel = new Label(manager)
                            {
                                Text = "Configuration",
                            },
                            closeButton = new Button(Manager)
                            {
                                StyleName = "icon",
                                Icon = IconFont.TimesCircle,
                                AnchorFrom = BoxAlignment.Centre,
                                AnchorTo = BoxAlignment.Centre,
                                CanGrow = false,
                            },
                        },
                    },
                    new ScrollArea(manager, configFieldsLayout = new LinearLayout(manager)
                    {
                        FitChildren = true,
                    }),
                },
            });

            closeButton.OnClick += (sender, e) =>
            {
                Effect = null;
                Displayed = false;
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Effect = null;
            }
            base.Dispose(disposing);
        }

        protected override void Layout()
        {
            base.Layout();
            layout.Size = Size;
        }

        private void Effect_OnChanged(object sender, EventArgs e) => updateEffect();
        private void Effect_OnConfigFieldsChanged(object sender, EventArgs e) => updateFields();

        private void updateEffect()
        {
            if (effect == null) return;
            titleLabel.Text = $"Configuration: {effect.Name} ({effect.BaseName})";
        }

        private void updateFields()
        {
            configFieldsLayout.ClearWidgets();
            if (effect == null) return;

            foreach (var field in effect.Config.SortedFields)
            {
                configFieldsLayout.Add(new LinearLayout(Manager)
                {
                    AnchorFrom = BoxAlignment.Centre,
                    AnchorTo = BoxAlignment.Centre,
                    Horizontal = true,
                    Fill = true,
                    Children = new Widget[]
                    {
                        new Label(Manager)
                        {
                            StyleName = "listItem",
                            Text = field.DisplayName,
                            AnchorFrom = BoxAlignment.TopLeft,
                            AnchorTo = BoxAlignment.TopLeft,
                        },
                        buildFieldEditor(field),
                    }
                });
            }
        }

        private Widget buildFieldEditor(EffectConfig.ConfigField field)
        {
            if (field.AllowedValues != null)
            {
                var widget = new Selectbox(Manager)
                {
                    Value = field.Value,
                    Options = field.AllowedValues,
                    AnchorFrom = BoxAlignment.Right,
                    AnchorTo = BoxAlignment.Right,
                    CanGrow = false,
                };
                widget.OnValueChanged += (sender, e) => setFieldValue(field, widget.Value);
                return widget;
            }
            else if (field.Type == typeof(bool))
            {
                var widget = new Selectbox(Manager)
                {
                    Value = field.Value,
                    Options = new NamedValue[]
                    {
                        new NamedValue() { Name = true.ToString(), Value = true, },
                        new NamedValue() { Name = false.ToString(), Value = false, },
                    },
                    AnchorFrom = BoxAlignment.Right,
                    AnchorTo = BoxAlignment.Right,
                    CanGrow = false,
                };
                widget.OnValueChanged += (sender, e) => setFieldValue(field, widget.Value);
                return widget;
            }
            else if (field.Type == typeof(string))
            {
                var widget = new Textbox(Manager)
                {
                    Value = field.Value?.ToString(),
                    AnchorFrom = BoxAlignment.Right,
                    AnchorTo = BoxAlignment.Right,
                    AcceptMultiline = true,
                    CanGrow = false,
                };
                widget.OnValueCommited += (sender, e) =>
                {
                    setFieldValue(field, widget.Value);
                    widget.Value = effect.Config.GetValue(field.Name).ToString();
                };
                return widget;
            }
            else if (field.Type == typeof(Vector2))
            {
                var widget = new Vector2Picker(Manager)
                {
                    Value = (Vector2)field.Value,
                    AnchorFrom = BoxAlignment.Right,
                    AnchorTo = BoxAlignment.Right,
                    CanGrow = false,
                };
                widget.OnValueCommited += (sender, e) =>
                {
                    setFieldValue(field, widget.Value);
                    widget.Value = (Vector2)effect.Config.GetValue(field.Name);
                };
                return widget;
            }
            else if (field.Type == typeof(Color4))
            {
                var widget = new HsbColorPicker(Manager)
                {
                    Value = (Color4)field.Value,
                    AnchorFrom = BoxAlignment.Right,
                    AnchorTo = BoxAlignment.Right,
                    CanGrow = false,
                };
                widget.OnValueCommited += (sender, e) =>
                {
                    setFieldValue(field, widget.Value);
                    widget.Value = (Color4)effect.Config.GetValue(field.Name);
                };
                return widget;
            }
            else if (field.Type.GetInterface(nameof(IConvertible)) != null)
            {
                var widget = new Textbox(Manager)
                {
                    Value = Convert.ToString(field.Value, CultureInfo.InvariantCulture),
                    AnchorFrom = BoxAlignment.Right,
                    AnchorTo = BoxAlignment.Right,
                    CanGrow = false,
                };
                widget.OnValueCommited += (sender, e) =>
                {
                    try
                    {
                        var value = Convert.ChangeType(widget.Value, field.Type, CultureInfo.InvariantCulture);
                        setFieldValue(field, value);
                    }
                    catch { }
                    widget.Value = Convert.ToString(effect.Config.GetValue(field.Name), CultureInfo.InvariantCulture);
                };
                return widget;
            }

            return new Label(Manager)
            {
                StyleName = "listItem",
                Text = field.Value.ToString(),
                Tooltip = $"Values of type {field.Type.Name} cannot be edited",
                AnchorFrom = BoxAlignment.Right,
                AnchorTo = BoxAlignment.Right,
                CanGrow = false,
            };
        }

        private void setFieldValue(EffectConfig.ConfigField field, object value)
        {
            if (effect.Config.SetValue(field.Name, value))
                effect.Refresh();
        }
    }
}
