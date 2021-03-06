﻿using CoreComponents;
using CoreComponents.Support;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using static CoreComponents.Singletons;

namespace Larry
{
    public static class GeometryHelper
    {
        public static Geometry GetGeometryFromWays(List<WayData> shapeList, OsmSharp.Relation r)
        {
            //A common-ish case looks like the outer entries are lines that join togetehr, and inner entries are polygons.
            //Let's see if we can build a polygon (or more, possibly)
            List<Coordinate> possiblePolygon = new List<Coordinate>();
            //from the first line, find the line that starts with the same endpoint (or ends with the startpoint, but reverse that path).
            //continue until a line ends with the first node. That's a closed shape.

            List<Polygon> existingPols = new List<Polygon>();
            List<Polygon> innerPols = new List<Polygon>();

            if (shapeList.Count == 0)
            {
                Log.WriteLog("Relation " + r.Id + " " + Place.GetPlaceName(r.Tags) + " has 0 ways in shapelist", Log.VerbosityLevels.High);
                return null;
            }

            //Separate sets
            var innerEntries = r.Members.Where(m => m.Role == "inner").Select(m => m.Id).ToList(); //these are almost always closed polygons.
            var outerEntries = r.Members.Where(m => m.Role == "outer").Select(m => m.Id).ToList();
            var innerPolys = new List<WayData>();

            if (innerEntries.Count() + outerEntries.Count() > shapeList.Count)
            {
                Log.WriteLog("Relation " + r.Id + " " + Place.GetPlaceName(r.Tags) + " is missing Ways, odds of success are low.", Log.VerbosityLevels.High);
            }

            //Not all ways are tagged for this, so we can't always rely on this.
            if (outerEntries.Count > 0)
                shapeList = shapeList.Where(s => outerEntries.Contains(s.id)).ToList();

            if (innerEntries.Count > 0)
            {
                innerPolys = shapeList.Where(s => innerEntries.Contains(s.id)).ToList();
                //foreach (var ie in innerPolys)
                //{
                while (innerPolys.Count() > 0)
                    //TODO: confirm all of these are closed shapes.
                    innerPols.Add(GetShapeFromLines(ref innerPolys));
                //}
            }

            //Remove any closed shapes first from the outer entries.
            var closedShapes = shapeList.Where(s => s.nds.First().id == s.nds.Last().id).ToList();
            foreach (var cs in closedShapes)
            {
                if (cs.nds.Count() > 3) //TODO: if SimplifyAreas is true, this might have been a closedShape that became a linestring or point from this.
                {
                    shapeList.Remove(cs);
                    existingPols.Add(factory.CreatePolygon(cs.nds.Select(n => new Coordinate(n.lon, n.lat)).ToArray()));
                }
                else
                    Log.WriteLog("Invalid closed shape found: " + cs.id);
            }

            while (shapeList.Count() > 0)
                existingPols.Add(GetShapeFromLines(ref shapeList)); //only outers here.

            existingPols = existingPols.Where(e => e != null).ToList();

            if (existingPols.Count() == 0)
            {
                Log.WriteLog("Relation " + r.Id + " " + Place.GetPlaceName(r.Tags) + " has no polygons and no lines that make polygons. Is this relation supposed to be an open line?", Log.VerbosityLevels.High);
                return null;
            }

            if (existingPols.Count() == 1)
            {
                //remove inner polygons
                var returnData = existingPols.First();
                foreach (var ir in innerPolys)
                {
                    if (ir.nds.First().id == ir.nds.Last().id)
                    {
                        var innerP = factory.CreateLineString(Converters.WayToCoordArray(ir));
                        returnData.InteriorRings.Append(innerP);
                    }
                }
                return returnData;
            }

            //return a multipolygon instead.
            Geometry multiPol = factory.CreateMultiPolygon(existingPols.Distinct().ToArray());
            //A new attempt at removing inner entries from outer ones via multipolygon.
            if (innerPols.Count() > 0)
            {
                var innerMultiPol = factory.CreateMultiPolygon(innerPols.Where(ip => ip != null).Distinct().ToArray());
                try
                {
                    multiPol = multiPol.Difference(innerMultiPol);
                }
                catch (Exception ex)
                {
                    Log.WriteLog("Relation " + r.Id + " Error trying to pull difference from inner and outer polygons:" + ex.Message);
                }
            }
            return multiPol;
        }
        public static Polygon GetShapeFromLines(ref List<WayData> shapeList)
        {
            //takes shapelist as ref, returns a polygon, leaves any other entries in shapelist to be called again.
            //NOTE/TODO: if this is a relation of lines that aren't a polygon (EX: a very long hiking trail), this should probably return the combined linestring?
            //TODO: if the lines are too small, should I return a Point instead?

            List<Coordinate> possiblePolygon = new List<Coordinate>();
            var firstShape = shapeList.FirstOrDefault();
            if (firstShape == null)
            {
                Log.WriteLog("shapelist has 0 ways in shapelist?", Log.VerbosityLevels.High);
                return null;
            }
            shapeList.Remove(firstShape);
            var nextStartnode = firstShape.nds.Last();
            var closedShape = false;
            var isError = false;
            possiblePolygon.AddRange(firstShape.nds.Where(n => n.id != nextStartnode.id).Select(n => new Coordinate(n.lon, n.lat)).ToList());
            while (closedShape == false)
            {
                var allPossibleLines = shapeList.Where(s => s.nds.First().id == nextStartnode.id).ToList();
                if (allPossibleLines.Count > 1)
                {
                    Log.WriteLog("Shape has multiple possible lines to follow, might not process correctly.", Log.VerbosityLevels.High);
                }
                var lineToAdd = shapeList.Where(s => s.nds.First().id == nextStartnode.id && s.nds.First().id != s.nds.Last().id).FirstOrDefault();
                if (lineToAdd == null)
                {
                    //check other direction
                    var allPossibleLinesReverse = shapeList.Where(s => s.nds.Last().id == nextStartnode.id).ToList();
                    if (allPossibleLinesReverse.Count > 1)
                    {
                        Log.WriteLog("Way has multiple possible lines to follow, might not process correctly (Reversed Order).");
                    }
                    lineToAdd = shapeList.Where(s => s.nds.Last().id == nextStartnode.id && s.nds.First().id != s.nds.Last().id).FirstOrDefault();
                    if (lineToAdd == null)
                    {
                        //If all lines are joined and none remain, this might just be a relation of lines. Return a combined element
                        Log.WriteLog("shape doesn't seem to have properly connecting lines, can't process as polygon.", Log.VerbosityLevels.High);
                        closedShape = true; //rename this to something better for breaking the loop
                        isError = true; //rename this to something like IsPolygon
                    }
                    else
                        lineToAdd.nds.Reverse();
                }
                if (!isError)
                {
                    possiblePolygon.AddRange(lineToAdd.nds.Where(n => n.id != nextStartnode.id).Select(n => new Coordinate(n.lon, n.lat)).ToList());
                    nextStartnode = lineToAdd.nds.Last();
                    shapeList.Remove(lineToAdd);

                    if (possiblePolygon.First().Equals(possiblePolygon.Last()))
                        closedShape = true;
                }
            }
            if (isError)
                return null;

            if (possiblePolygon.Count <= 3)
            {
                Log.WriteLog("Didn't find enough points to turn into a polygon. Probably an error.", Log.VerbosityLevels.High);
                return null;
            }

            var poly = factory.CreatePolygon(possiblePolygon.ToArray());
            poly = GeometrySupport.CCWCheck(poly);
            if (poly == null)
            {
                Log.WriteLog("Found a shape that isn't CCW either way. Error.", Log.VerbosityLevels.High);
                return null;
            }
            return poly;
        }
    }
}
