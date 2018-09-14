// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu/master/LICENCE

using System;
using System.Linq;
using osu.Framework.MathUtils;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Catch.Objects;
using osu.Game.Rulesets.Catch.UI;
using osu.Game.Rulesets.Replays;
using osu.Game.Users;

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
            public double position {get;} = 0.5;
            public double time {get;} = 0;
            public double hyperdashTarget {get;} = -1;
            //The value we get by catching this object (internal)
            //100 for fruits, 1 else.
            public int value{get;} = 0;
            //maxium score of the left path (next note strictly to the left)
            public int scoreLeft{get;set;} = -1;
            //maximum score of the right path ( next note same position or right)
            public int scoreRight{get;set;} = -1;
            //limite between right path and right path.
            public double limiter{get;set;} = -1;

            public int bestLeftIndex{get;set;} = -1;
            public int bestLeftPath{get;set;} = -2;
            public int bestRightIndex{get;set;} = -1;
            public int bestRightPath{get;set;} = -2;
        };

        public override Replay Generate()
        {
            // todo: add support for HT DT
            const double dash_speed = CatcherArea.Catcher.BASE_SPEED;
            const double movement_speed = dash_speed / 2;

            double catcherHalfWidth = CatcherArea.GetCatcherSize(Beatmap.BeatmapInfo.BaseDifficulty) / 2;

            // Todo: Realistically this shouldn't be needed, but the first frame is skipped with the way replays are currently handled
            Replay.Frames.Add(new CatchReplayFrame(-100000, 0.5));

            //First, we establish a sorted list of catchable of objects.
            objects.Add(new AutoCatchObject()); //fictive object for starting position;
            foreach(var obj in Beatmap.HitOjects)
                if(obj is BananaShower || obj is JuiceStream)
                {
                    foreach(var nested in obj)
                    objects.Add(new AutoCatchObject(nested));
                }
                else
                    objects.Add(new AutoCatchObject(obj));
            objects.Sort((h1, h2) => h1.time.CompareTo(h2.time));

            //Compute scores value with dynamic programming
            void ComputeScore(int currentIndex)
            {
                AutoCatchObject current = objects[currentIndex];
                //if score is already computed, exit
                if(current.scoreLeft != -1)
                    return;
                //Compute left path value
                double X = Math.Max(0,current.position - catcherHalfWidth);
                double timeHorizon = X / dash_speed;
                int nextTargetIndex = currentIndex;
                AutoCatchObject nextTarget = objects[nextTargetIndex];
                while(nextTarget.time < timeHorizon || current.scoreLeft == -1)
                {
                    nextTargetIndex++;
                    if(nextTargetIndex >= objects.Count)
                        break;
                    nextTarget = objects[nextTargetIndex];
                    if(nextTarget.position >= current.position) //only left path
                        continue; 
                    ComputeScore(nextTargetIndex);
                    if(nextTarget.scoreLeft > nextTarget.scoreRight && //we want the left path of next target
                    nextTarget.scoreLeft + nextTarget.value > current.scoreLeft &&
                    (X - (nextTarget.time - current.time) * dash_speed < nextTarget.limiter || //we can reach it
                    (current.hyperdashTarget >= 0 && current.hyperdashTarget < nextTarget.limiter)))
                    {
                        current.scoreLeft = nextTarget.scoreLeft + nextTarget.value;
                        current.limiter = current.hyperdashTarget > 0 ? 1.0 : nextTarget.limiter + (nextTarget.time - current.time) * dash_speed;
                        timeHorizon = Math.Max(nextTarget.position, current.position - nextTarget.position) / dash_speed;
                        bestLeftIndex = nextTargetIndex;
                        bestLeftPath = -1;
                    }
                    else if(nextTarget.scoreRight + nextTarget.value > current.scoreLeft)
                    {
                        current.scoreLeft = nextTarget.scoreRight + nextTarget.value;
                        current.limiter = current.hyperdashTarget > 0 ? 1.0 : nextTarget.position + catcherHalfWidth + (nextTarget.time - current.time) * dash_speed;
                        timeHorizon = Math.Max(nextTarget.position, current.position - nextTarget.position) / dash_speed;
                        bestLeftIndex = nextTargetIndex;
                        bestLeftPath = 1;
                    }
                }
                if(current.scoreLeft == -1) //there's nothing to catch after current on the left
                {
                    current.scoreLeft = 0;
                    current.limiter = 0.0;
                }

                //compute the rightpath value
                X = Math.Min(1,current.position + catcherHalfWidth);
                timeHorizon = (1.0 - X) / dash_speed;
                nextTargetIndex = currentIndex;
                AutoCatchObject nextTarget = objects[nextTargetIndex];
                while(nextTarget.time < timeHorizon || current.scoreRight == -1)
                {
                    nextTargetIndex++;
                    if(nextTargetIndex >= objects.Count)
                        break;
                    nextTarget = objects[nextTargetIndex];
                    if(nextTarget.position < current.position) //only right path
                        continue; 
                    ComputeScore(nextTargetIndex);
                    if(nextTarget.scoreRight > nextTarget.scoreLeft && //we want the right path of next target
                    nextTarget.scoreRight + nextTarget.value > current.scoreRight &&
                    (X + (nextTarget.time - current.time) * dash_speed > nextTarget.limiter || //we can reach it
                     current.hyperdashTarget > nextTarget.limiter))
                    {
                        current.scoreRight = nextTarget.scoreRight + nextTarget.value;
                        if(current.scoreRight > current.scoreLeft)
                            current.limiter = current.hyperdashTarget > 0 ? 0.0 : nextTarget.limiter - (nextTarget.time - current.time) * dash_speed;
                        timeHorizon = Math.Max(nextTarget.position, current.position - nextTarget.position) / dash_speed;
                        bestRightIndex = nextTargetIndex;
                        bestRightPath = 1;
                    }
                    else if(nextTarget.scoreLeft + nextTarget.value > current.scoreRight)
                    {
                        current.scoreRight = nextTarget.scoreLeft + nextTarget.value;
                        if(current.scoreRight > current.scoreLeft)
                            current.limiter = current.hyperdashTarget > 0 ? 0.0 : nextTarget.position - catcherHalfWidth + (nextTarget.time - current.time) * dash_speed;
                        timeHorizon = Math.Max(nextTarget.position, current.position - nextTarget.position) / dash_speed;
                        bestRightIndex = nextTargetIndex;
                        bestRightPath = -1;
                    }
                }
                if(current.scoreRight == -1) //there's nothing to catch after current on the right
                    current.scoreRight = 0;
            }
            for(int i=0; i<objects.Count; ++i)
                ComputeScore(i);
            
            //Generate the frames associated with the best path, recursively
            void CatchNextObject(int currentIndex, double currentX, int lastDirection)
            {
                Assert(minX <= maxX);
                var obj = Objects[currentIndex];
                Replay.Frames.Add(new CatchReplayFrame(obj.time, currentX));
                if(obj.scoreLeft >= obj.scoreRight && lastDirection == 1 || currentX < obj.limiter)//we want to go left and we can
                {
                    Catch
                }

            }
            //We start in 0.5, middle of our starting fictive object. 
            //In theory, it is possible that is is impossible to take neither the right path nor the left path from the middle,
            //but we assume that the left path is always possible. Worst case scenario, our first dash is surrealist.
            //TLDR : we don't have a lastDirction at first be we assume that it's right.
            CatchNextObject(0, 0.5, 1);
            return Replay;
        }
    }
}
