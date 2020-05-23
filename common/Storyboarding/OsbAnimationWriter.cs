﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StorybrewCommon.Storyboarding.Commands;
using StorybrewCommon.Storyboarding.CommandValues;
using StorybrewCommon.Storyboarding.Display;

namespace StorybrewCommon.Storyboarding
{
    class OsbAnimationWriter : OsbSpriteWriter
    {
        private OsbAnimation OsbAnimation;
        public OsbAnimationWriter(OsbAnimation osbAnimation, AnimatedValue<CommandPosition> moveTimeline,
                                                             AnimatedValue<CommandDecimal> moveXTimeline,
                                                             AnimatedValue<CommandDecimal> moveYTimeline,
                                                             AnimatedValue<CommandDecimal> scaleTimeline,
                                                             AnimatedValue<CommandScale> scaleVecTimeline,
                                                             AnimatedValue<CommandDecimal> rotateTimeline,
                                                             AnimatedValue<CommandDecimal> fadeTimeline,
                                                             AnimatedValue<CommandColor> colorTimeline,
                                                             TextWriter writer, ExportSettings exportSettings, OsbLayer layer)
                                        : base(osbAnimation, moveTimeline,
                                                             moveXTimeline,
                                                             moveYTimeline,
                                                             scaleTimeline,
                                                             scaleVecTimeline,
                                                             rotateTimeline,
                                                             fadeTimeline,
                                                             colorTimeline,
                                                             writer, exportSettings, layer)
        {
            OsbAnimation = osbAnimation;
        }

        protected override OsbSprite CreateSprite(List<ICommand> segment)
        {
            if (OsbAnimation.LoopType == OsbLoopType.LoopOnce && segment.Min(c => c.StartTime) >= OsbAnimation.AnimationEndtime())
            {
                //this shouldn't loop again so we need a sprite instead
                return base.CreateSprite(segment);
            }
            else
            {
                var animation = new OsbAnimation()
                {
                    TexturePath = OsbAnimation.TexturePath,
                    InitialPosition = OsbAnimation.InitialPosition,
                    Origin = OsbAnimation.Origin,
                    FrameCount = OsbAnimation.FrameCount,
                    FrameDelay = OsbAnimation.FrameDelay,
                    LoopType = OsbAnimation.LoopType,
                };

                foreach (var command in segment)
                    animation.AddCommand(command);

                return animation;
            }
        }

        protected override void WriteHeader(OsbSprite sprite)
        {
            var animation = (OsbAnimation)sprite;
            double frameDelay = animation.FrameDelay;
            TextWriter.WriteLine($"Animation,{OsbLayer},{animation.Origin},\"{OsbSprite.TexturePath.Trim()}\",{animation.InitialPosition.X.ToString(ExportSettings.NumberFormat)},{animation.InitialPosition.Y.ToString(ExportSettings.NumberFormat)},{animation.FrameCount},{frameDelay.ToString(ExportSettings.NumberFormat)},{animation.LoopType}");
        }

        protected override HashSet<int> GetFragmentationTimes()
        {
            HashSet<int> fragmentationTimes = base.GetFragmentationTimes();

            int tMax = fragmentationTimes.Max();

            for (double d = OsbAnimation.StartTime; d < OsbAnimation.AnimationEndtime(); d += OsbAnimation.LoopDuration())
            {
                fragmentationTimes.RemoveWhere(t => t > d && t < d + OsbAnimation.LoopDuration() && t < tMax);
            }

            return fragmentationTimes;
        }
    }

    static class OsbAnimationExtensions
    {
        public static double AnimationEndtime(this OsbAnimation osbAnimation)
        {
            if (osbAnimation.LoopType == OsbLoopType.LoopOnce)
                return osbAnimation.StartTime + osbAnimation.LoopDuration();
            else
                return osbAnimation.EndTime;
        }

        public static double LoopDuration(this OsbAnimation osbAnimation)
        {
            return osbAnimation.FrameCount * osbAnimation.FrameDelay;
        }
    }
}
