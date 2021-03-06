﻿using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using static CoreComponents.ConstantValues;
using static CoreComponents.DbTables;

namespace CoreComponents
{
    public static class ScoreData
    {
        //Default Scoring rules:
        //Each Cell10 of surface area is 1 Score (would be Points in any other game, but Points is already an overloaded term in this system).
        //OSM Areas are measured in square area, divided by Cell10 area squared. (An area that covers 25 square Cell10s is 25 Score)
        //Lines are measured in their length.  (A trail that's 25 * resolutionCell10 long is 25 Score)
        //OSM points are assigned a Score of 1 as the minimum interactable size object. 

        public static string GetScoresForArea(Geometry areaPoly, List<MapData> places)
        {
            //Determines the Scores for the Places, limited to the intersection of the current Area. 1 Cell10 = 1 Score.
            //EX: if a park overlaps 800 Cell10s, but the current area overlaps 250 of them, this returns 250 for that park.
            //Lists each Place and its corresponding Score.
            List<Tuple<string, int, long>> areaSizes = new List<Tuple<string, int, long>>();
            foreach (var md in places)
            {
                var containedArea = md.place.Intersection(areaPoly);
                var areaCell10Count = GetScoreForSinglePlace(containedArea);
                areaSizes.Add(new Tuple<string, int, long>(md.name, areaCell10Count, md.MapDataId));
            }
            return string.Join(Environment.NewLine, areaSizes.Select(a => a.Item1 + "|" + a.Item2 + "|" + a.Item3));
        }

        public static string GetScoresForFullArea(List<MapData> places)
        {
            //As above, but counts the Places' full area, not the area in the given Cell8 or Cell10. 
            Dictionary<string, int> areaSizes = new Dictionary<string, int>();
            foreach (var place in places)
            {
                areaSizes.Add(place.name, GetScoreForSinglePlace(place.place));
            }
            return string.Join(Environment.NewLine, areaSizes.Select(a => a.Key + "|" + a.Value));
        }

        public static int GetScoreForSinglePlace(Geometry place)
        {
            //The core function for scoring.
            var containedAreaSize = place.Area; //The area, in square degrees
            if (containedAreaSize == 0)
            {
                //This is a line or a point, it has no area so we need to fix the calculations to match the display grid.
                //Points will always be 1.
                //Lines will be based on distance.
                if (place is NetTopologySuite.Geometries.Point)
                    containedAreaSize = squareCell10Area;
                else if (place is NetTopologySuite.Geometries.LineString)
                    containedAreaSize = ((LineString)place).Length * resolutionCell10;
                //This gives us the length of the line in Cell10 lengths, which may be slightly different from the number of Cell10 draws on the map as belonging to this line.
            }
            var containedAreaCell10Count = (int)Math.Round(containedAreaSize / squareCell10Area);
            if (containedAreaCell10Count == 0)
                containedAreaCell10Count = 1;

            return containedAreaCell10Count;
        }

    }
}
