// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu/master/LICENCE

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.MathUtils;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Catch.Objects;
using osu.Game.Rulesets.Catch.UI;
using osu.Game.Rulesets.Replays;
using osu.Game.Users;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.Catch.Replays
{
    internal class CatchAutoGenerator : AutoGenerator<CatchHitObject>
    {
        public CatchAutoGenerator(Beatmap<CatchHitObject> beatmap)
            : base(beatmap)
        {
            Replay = new Replay { User = new User { Username = @"osu!salad!" } };
        }

        protected Replay Replay;

        private class AutoCatchObject
        {
            public AutoCatchObject(CatchHitObject obj)
            {
                position = obj.X;
                time = obj.StartTime;
                hyperdashTarget = obj.HyperDash ? obj.HyperDashTarget.X : -1.0;
                value = obj is Fruit ? 100 : 1;
            }

            public AutoCatchObject() { }

            public double position { get; } = 0.5;
            public double time { get; } = 0;
            public double hyperdashTarget { get; } = -1;
            //The value we get by catching this object (internal)
            //100 for fruits, 1 else.
            public int value { get; } = 0;
            //maxium score of the left path (next note strictly to the left)
            public int scoreLeft { get; set; } = -1;
            //maximum score of the right path ( next note same position or right)
            public int scoreRight { get; set; } = -1;
            //limite between right path and right path.
            public double limiterLeft { get; set; } = 1;
            public double limiterRight { get; set; } = 0;

            public double nextTargetRight = -1;
            public double nextTargetLeft = -1;
            public double nextPathRight = -2;
            public double nextPathLeft = -2;
        };

        public override Replay Generate()
        {
            // todo: add support for HT DT
            const double dash_speed = CatcherArea.Catcher.BASE_SPEED;
            //const double movement_speed = dash_speed / 2;

            double catcherHalfWidth = CatcherArea.GetCatcherSize(Beatmap.BeatmapInfo.BaseDifficulty) / 2;

            // Todo: Realistically this shouldn't be needed, but the first frame is skipped with the way replays are currently handled
            Replay.Frames.Add(new CatchReplayFrame(-100000, 0.5f));

            List<AutoCatchObject> objects = new List<AutoCatchObject>();

            //First, we establish a sorted list of catchable of objects.
            objects.Add(new AutoCatchObject()); //fictive object for starting position;
            foreach (var obj in Beatmap.HitObjects)
                if (obj is BananaShower || obj is JuiceStream)
                {
                    foreach (var nested in obj.NestedHitObjects)
                        objects.Add(new AutoCatchObject((CatchHitObject)nested));
                }
                else
                    objects.Add(new AutoCatchObject(obj));
            objects.Sort((h1, h2) => h1.time.CompareTo(h2.time));

            //Compute scores value with dynamic programming
            void ComputeScore(int currentIndex)
            {
                AutoCatchObject current = objects[currentIndex];
                //if score is already computed, exit
                if (current.scoreLeft != -1)
                    return;
                //Compute left path value
                double X = Math.Max(0, current.position - catcherHalfWidth);
                double timeHorizon = current.time + 2 * current.position / dash_speed;
                int nextTargetIndex = currentIndex;
                AutoCatchObject nextTarget = objects[nextTargetIndex];
                while (nextTarget.time < timeHorizon || current.scoreLeft == -1)
                {
                    nextTargetIndex++;
                    if (nextTargetIndex >= objects.Count)
                        break;
                    nextTarget = objects[nextTargetIndex];
                    if (nextTarget.position >= current.position
                    || (X - (nextTarget.time - current.time) * dash_speed > nextTarget.position + catcherHalfWidth && current.hyperdashTarget != nextTarget.position)) //only left path
                        continue;
                    ComputeScore(nextTargetIndex);
                    if (nextTarget.scoreLeft > nextTarget.scoreRight && //we want the left path of next target
                    nextTarget.scoreLeft + nextTarget.value > current.scoreLeft &&
                    (X - (nextTarget.time - current.time) * dash_speed < nextTarget.limiterLeft || //we can reach it
                    current.hyperdashTarget == nextTarget.position))
                    {
                        current.scoreLeft = nextTarget.scoreLeft + nextTarget.value;
                        current.limiterLeft = current.hyperdashTarget > 0 ? 1 : nextTarget.limiterLeft + (nextTarget.time - current.time) * dash_speed;
                        current.nextTargetLeft = nextTarget.position;
                        current.nextPathLeft = -1;
                    }
                    else if (nextTarget.scoreRight + nextTarget.value > current.scoreLeft)
                    {
                        current.scoreLeft = nextTarget.scoreRight + nextTarget.value;
                        current.limiterLeft = current.hyperdashTarget > 0 ? 1 : nextTarget.position + catcherHalfWidth + (nextTarget.time - current.time) * dash_speed;
                        current.nextTargetLeft = nextTarget.position;
                        current.nextPathLeft = 1;
                    }
                }
                if (current.scoreLeft == -1) //there's nothing to catch after current on the left
                    current.scoreLeft = 0;

                //compute the rightpath value
                X = Math.Min(1, current.position + catcherHalfWidth);
                timeHorizon = current.time + 2 * (1.0 - current.position) / dash_speed;
                nextTargetIndex = currentIndex;
                nextTarget = objects[nextTargetIndex];
                while (nextTarget.time < timeHorizon || current.scoreRight == -1)
                {
                    nextTargetIndex++;
                    if (nextTargetIndex >= objects.Count)
                        break;
                    nextTarget = objects[nextTargetIndex];
                    if (nextTarget.position < current.position
                    || (X + (nextTarget.time - current.time) * dash_speed < nextTarget.position - catcherHalfWidth && current.hyperdashTarget != nextTarget.position)) //only right path reachable objects
                        continue;
                    ComputeScore(nextTargetIndex);
                    if (nextTarget.scoreRight > nextTarget.scoreLeft && //we want the right path of next target
                    nextTarget.scoreRight + nextTarget.value > current.scoreRight &&
                    (X + (nextTarget.time - current.time) * dash_speed > nextTarget.limiterRight || //we can reach it
                     current.hyperdashTarget == nextTarget.position))
                    {
                        current.scoreRight = nextTarget.scoreRight + nextTarget.value;
                        current.limiterRight = current.hyperdashTarget > 0 ? 0 : nextTarget.limiterRight - (nextTarget.time - current.time) * dash_speed;
                        current.nextTargetRight = nextTarget.position;
                        current.nextPathRight = -1;
                    }
                    else if (nextTarget.scoreLeft + nextTarget.value > current.scoreRight)
                    {
                        current.scoreRight = nextTarget.scoreLeft + nextTarget.value;
                        current.limiterRight = current.hyperdashTarget > 0 ? 0 : nextTarget.position - catcherHalfWidth - (nextTarget.time - current.time) * dash_speed;
                        current.nextTargetRight = nextTarget.position;
                        current.nextPathRight = 1;
                    }
                }
                if (current.scoreRight == -1) //there's nothing to catch after current on the right
                    current.scoreRight = 0;
            }
            ComputeScore(0);
            Logger.Log("Score computed");
            double position = 0.5;
            for (int i = 0; i < objects.Count;)
            {

                AutoCatchObject current = objects[i];
                Logger.Log("i =" + i + " " + position + " ");
                Logger.Log("" + current.limiterLeft + " " + current.limiterRight + " " + current.scoreLeft + " " + current.scoreRight);
                Replay.Frames.Add(new CatchReplayFrame(current.time, (float)current.position));
                if (position - catcherHalfWidth > current.limiterLeft || current.nextTargetLeft == -1 || (position + catcherHalfWidth >= current.limiterRight && current.scoreRight >= current.scoreLeft))
                    position = current.nextTargetRight;
                else if (position + catcherHalfWidth < current.limiterRight || current.nextTargetRight == -1 || (position - catcherHalfWidth <= current.limiterLeft && current.scoreLeft > current.scoreRight))
                    position = current.nextTargetLeft;
                if (position < 0)
                    break;

                do
                {
                    ++i;
                } while (i < objects.Count && objects[i].position != position);
            }
            return Replay;
        }
    }
}
