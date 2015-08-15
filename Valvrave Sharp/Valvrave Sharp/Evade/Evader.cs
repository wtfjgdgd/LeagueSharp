﻿namespace Valvrave_Sharp.Evade
{
    using System.Collections.Generic;
    using System.Linq;

    using LeagueSharp;
    using LeagueSharp.SDK.Clipper;
    using LeagueSharp.SDK.Core;
    using LeagueSharp.SDK.Core.Extensions;
    using LeagueSharp.SDK.Core.Extensions.SharpDX;
    using LeagueSharp.SDK.Core.Math.Polygons;
    using LeagueSharp.SDK.Core.Utils;

    using SharpDX;

    internal class Evader
    {
        #region Static Fields

        public static int LastWardJumpAttempt = 0;

        #endregion

        #region Public Methods and Operators

        public static List<Vector2> GetEvadePoints(
            int speed = -1,
            int delay = 0,
            bool isBlink = false,
            bool onlyGood = false)
        {
            speed = speed == -1 ? (int)Program.Player.MoveSpeed : speed;
            var goodCandidates = new List<Vector2>();
            var badCandidates = new List<Vector2>();
            var polygonList = Evade.DetectedSkillshots.Where(i => i.Evade).Select(i => i.EvadePolygon).ToList();
            var dangerPolygons = ClipPolygons(polygonList).ToPolygons();
            var myPosition = Program.Player.ServerPosition.ToVector2();
            foreach (var poly in dangerPolygons)
            {
                for (var i = 0; i <= poly.Points.Count - 1; i++)
                {
                    var sideStart = poly.Points[i];
                    var sideEnd = poly.Points[i == poly.Points.Count - 1 ? 0 : i + 1];
                    var originalCandidate = myPosition.ProjectOn(sideStart, sideEnd).SegmentPoint;
                    var distanceToEvadePoint = myPosition.Distance(originalCandidate);
                    if (distanceToEvadePoint >= 600)
                    {
                        continue;
                    }
                    var s = distanceToEvadePoint < 200 && sideEnd.Distance(sideStart) > 90
                                ? Config.DiagonalEvadePointsCount
                                : 0;
                    for (var j = -s; j <= s; j++)
                    {
                        var candidate = originalCandidate
                                        + j * Config.DiagonalEvadePointsStep * (sideEnd - sideStart).Normalized();
                        var pathToPoint =
                            Program.Player.GetPath(candidate.ToVector3()).Select(a => a.ToVector2()).ToList();
                        if (!isBlink)
                        {
                            if (IsSafePath(pathToPoint, Config.EvadingFirstTimeOffset, speed, delay).IsSafe)
                            {
                                goodCandidates.Add(candidate);
                            }
                            if (IsSafePath(pathToPoint, Config.EvadingSecondTimeOffset, speed, delay).IsSafe && j == 0)
                            {
                                badCandidates.Add(candidate);
                            }
                        }
                        else
                        {
                            if (IsSafeToBlink(pathToPoint[pathToPoint.Count - 1], Config.EvadingFirstTimeOffset, delay))
                            {
                                goodCandidates.Add(candidate);
                            }
                            if (IsSafeToBlink(pathToPoint[pathToPoint.Count - 1], Config.EvadingSecondTimeOffset, delay))
                            {
                                badCandidates.Add(candidate);
                            }
                        }
                    }
                }
            }
            return goodCandidates.Count > 0 ? goodCandidates : (onlyGood ? new List<Vector2>() : badCandidates);
        }

        public static List<Obj_AI_Base> GetEvadeTargets(
            EvadeSpellData spell,
            bool isBlink = false,
            bool onlyGood = false,
            bool dontCheckForSafety = false)
        {
            var badTargets = new List<Obj_AI_Base>();
            var goodTargets = new List<Obj_AI_Base>();
            var allTargets = new List<Obj_AI_Base>();
            foreach (var targetType in spell.ValidTargets)
            {
                switch (targetType)
                {
                    case SpellTargets.AllyChampions:
                        allTargets.AddRange(
                            GameObjects.AllyHeroes.Where(i => i.IsValidTarget(spell.MaxRange, false) && !i.IsMe));
                        break;
                    case SpellTargets.AllyMinions:
                        allTargets.AddRange(GameObjects.AllyMinions.Where(i => i.IsValidTarget(spell.MaxRange, false)));
                        break;
                    case SpellTargets.AllyWards:
                        allTargets.AddRange(GameObjects.AllyWards.Where(i => i.IsValidTarget(spell.MaxRange, false)));
                        break;
                    case SpellTargets.EnemyChampions:
                        allTargets.AddRange(GameObjects.EnemyHeroes.Where(i => i.IsValidTarget(spell.MaxRange)));
                        break;
                    case SpellTargets.EnemyMinions:
                        allTargets.AddRange(GameObjects.EnemyMinions.Where(i => i.IsValidTarget(spell.MaxRange)));
                        break;
                    case SpellTargets.EnemyWards:
                        allTargets.AddRange(GameObjects.EnemyWards.Where(i => i.IsValidTarget(spell.MaxRange)));
                        break;
                }
            }
            foreach (var target in
                allTargets.Where(i => dontCheckForSafety || IsSafePoint(i.ServerPosition.ToVector2()).IsSafe)
                    .Where(i => spell.Name != "YasuoDashWrapper" || !i.HasBuff("YasuoDashWrapper")))
            {
                if (isBlink)
                {
                    if (Variables.TickCount - LastWardJumpAttempt < 250
                        || IsSafeToBlink(target.ServerPosition.ToVector2(), Config.EvadingFirstTimeOffset, spell.Delay))
                    {
                        goodTargets.Add(target);
                    }
                    if (Variables.TickCount - LastWardJumpAttempt < 250
                        || IsSafeToBlink(target.ServerPosition.ToVector2(), Config.EvadingSecondTimeOffset, spell.Delay))
                    {
                        badTargets.Add(target);
                    }
                }
                else
                {
                    var pathToTarget = new List<Vector2>
                                           {
                                               Program.Player.ServerPosition.ToVector2(),
                                               target.ServerPosition.ToVector2()
                                           };
                    if (Variables.TickCount - LastWardJumpAttempt < 250
                        || IsSafePath(pathToTarget, Config.EvadingFirstTimeOffset, spell.Speed, spell.Delay).IsSafe)
                    {
                        goodTargets.Add(target);
                    }
                    if (Variables.TickCount - LastWardJumpAttempt < 250
                        || IsSafePath(pathToTarget, Config.EvadingSecondTimeOffset, spell.Speed, spell.Delay).IsSafe)
                    {
                        badTargets.Add(target);
                    }
                }
            }
            return goodTargets.Count > 0 ? goodTargets : (onlyGood ? new List<Obj_AI_Base>() : badTargets);
        }

        public static SafePathResult IsSafePath(List<Vector2> path, int timeOffset, int speed = -1, int delay = 0)
        {
            var isSafe = true;
            var intersections = new List<FoundIntersection>();
            var intersection = new FoundIntersection();
            foreach (var sResult in
                Evade.DetectedSkillshots.Where(i => i.Evade).Select(i => i.IsSafePath(path, timeOffset, speed, delay)))
            {
                isSafe = isSafe && sResult.IsSafe;
                if (sResult.Intersection.Valid)
                {
                    intersections.Add(sResult.Intersection);
                }
            }
            if (isSafe)
            {
                return new SafePathResult(true, intersection);
            }
            var sortedList = intersections.OrderBy(o => o.Distance).ToList();
            return new SafePathResult(false, sortedList.Count > 0 ? sortedList[0] : intersection);
        }

        public static IsSafeResult IsSafePoint(Vector2 point)
        {
            var result = new IsSafeResult { SkillshotList = new List<Skillshot>() };
            foreach (var skillshot in Evade.DetectedSkillshots.Where(i => i.Evade && !i.IsSafePoint(point)))
            {
                result.SkillshotList.Add(skillshot);
            }
            result.IsSafe = result.SkillshotList.Count == 0;
            return result;
        }

        public static bool IsSafeToBlink(Vector2 point, int timeOffset, int delay)
        {
            return Evade.DetectedSkillshots.Where(i => i.Evade).All(i => i.IsSafeToBlink(point, timeOffset, delay));
        }

        #endregion

        #region Methods

        private static List<List<IntPoint>> ClipPolygons(IReadOnlyCollection<Polygon> polygons)
        {
            var subj = new List<List<IntPoint>>(polygons.Count);
            var clip = new List<List<IntPoint>>(polygons.Count);
            foreach (var polygon in polygons)
            {
                subj.Add(polygon.ToClipperPath());
                clip.Add(polygon.ToClipperPath());
            }
            var solution = new List<List<IntPoint>>();
            var c = new Clipper();
            c.AddPaths(subj, PolyType.PtSubject, true);
            c.AddPaths(clip, PolyType.PtClip, true);
            c.Execute(ClipType.CtUnion, solution, PolyFillType.PftPositive, PolyFillType.PftEvenOdd);
            return solution;
        }

        #endregion

        public struct IsSafeResult
        {
            #region Fields

            public bool IsSafe;

            public List<Skillshot> SkillshotList;

            #endregion
        }
    }
}