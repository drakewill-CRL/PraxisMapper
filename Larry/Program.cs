﻿using CoreComponents;
using CoreComponents.Support;
using Google.OpenLocationCode;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using OsmSharp;
using OsmSharp.Streams;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using static CoreComponents.ConstantValues;
using static CoreComponents.DbTables;
using static CoreComponents.Place;
using static CoreComponents.Singletons;

//TODO: look into using Span<T> instead of lists? This might be worth looking at performance differences. (and/or Memory<T>, which might be a parent for Spans)
//TODO: Ponder using https://download.bbbike.org/osm/ as a data source to get a custom extract of an area (for when users want a local-focused app, probably via a wizard GUI)

namespace Larry
{
    class Program
    {
        static void Main(string[] args)
        {
            var memMon = new MemoryMonitor();
            PraxisContext.connectionString = ParserSettings.DbConnectionString;
            PraxisContext.serverMode = ParserSettings.DbMode;

            if (args.Count() == 0)
            {
                Console.WriteLine("You must pass an arguement to this application");
                //TODO: list valid commands or point at the docs file
                return;
            }

            if (args.Any(a => a.StartsWith("-dbMode:")))
            {
                //scan a file for information on what will or won't load.
                string arg = args.Where(a => a.StartsWith("-dbMode:")).First().Replace("-dbMode:", "");
                PraxisContext.serverMode = arg;
            }

            if (args.Any(a => a.StartsWith("-dbConString:")))
            {
                //scan a file for information on what will or won't load.
                string arg = args.Where(a => a.StartsWith("-dbConString:")).First().Replace("-dbConString:", "");
                PraxisContext.connectionString = arg;
            }

            //Check for settings flags first before running any commands.
            if (args.Any(a => a == "-v" || a == "-verbose"))
                Log.Verbosity = Log.VerbosityLevels.High;

            if (args.Any(a => a == "-noLogs"))
                Log.Verbosity = Log.VerbosityLevels.Off;

            //TODO: this should probably be the default.
            if (args.Any(a => a == "-processEverything"))
            {
                DbSettings.processRoads = true;
                DbSettings.processBuildings = true;
                DbSettings.processParking = true;
            }

            if (args.Any(a => a == "-spaceSaver"))
            {
                ParserSettings.UseHighAccuracy = false;
                factory = NtsGeometryServices.Instance.CreateGeometryFactory(new PrecisionModel(1000000), 4326); //SRID matches Plus code values.  Precision model means round all points to 7 decimal places to not exceed float's useful range.
                SimplifyAreas = true;
            }

            //If multiple args are supplied, run them in the order that make sense, not the order the args are supplied.

            if (args.Any(a => a == "-createDB")) //setup the destination database
            {
                DBCommands.MakePraxisDB();
            }

            if (args.Any(a => a == "-cleanDB"))
            {
                DBCommands.CleanDb();
            }

            if (args.Any(a => a == "-findServerBounds"))
            {
                DBCommands.FindServerBounds();
            }

            if (args.Any(a => a == "-singleTest"))
            {
                //Check on a specific thing. Not an end-user command.
                //Current task: Identify issue with relation
                SingleTest();
            }

            if (args.Any(a => a.StartsWith("-getPbf:")))
            {
                //Wants 3 pieces. Drops in placeholders if some are missing. Giving no parameters downloads Ohio.
                //TODO: confirm this works for places with fewer sub-divisions in the path.
                string arg = args.Where(a => a.StartsWith("-getPbf:")).First().Replace("-getPbf:", "");
                var splitData = arg.Split('|'); //remember the first one will be empty.
                string level1 = splitData.Count() >= 4 ? splitData[3] : "north-america";
                string level2 = splitData.Count() >= 3 ? splitData[2] : "us";
                string level3 = splitData.Count() >= 2 ? splitData[1] : "ohio";

                DownloadPbfFile(level1, level2, level3, ParserSettings.PbfFolder);
            }

            if (args.Any(a => a == "-resetXml" || a == "-resetPbf")) //update both anyways.
            {
                FileCommands.ResetFiles(ParserSettings.PbfFolder);
            }

            if (args.Any(a => a == "-resetJson"))
            {
                FileCommands.ResetFiles(ParserSettings.JsonMapDataFolder);
            }

            if (args.Any(a => a == "-trimPbfFiles"))
            {
                FileCommands.MakeAllSerializedFilesFromPBF();
            }

            if (args.Any(a => a.StartsWith("-trimPbfsByType")))
            {
                List<string> filenames = System.IO.Directory.EnumerateFiles(ParserSettings.PbfFolder, "*.pbf").ToList();
                foreach (string filename in filenames)
                    FileCommands.SerializeSeparateFilesFromPBF(filename);
            }

            if (args.Any(a => a.StartsWith("-lastChance")))
            {
                //split this arg
                var areaType = args.Where(a => a.StartsWith("-lastChance")).First().Split(":")[1];
                List<string> filenames = System.IO.Directory.EnumerateFiles(ParserSettings.PbfFolder, "*.pbf").ToList();
                foreach (string filename in filenames)
                    FileCommands.LastChanceSerializer(filename, areaType);
            }

            if (args.Any(a => a == "-readMapData"))
            {
                DBCommands.AddMapDataToDBFromFiles();
            }

            if (args.Any(a => a == "-updateDatabase"))
            {
                DBCommands.UpdateExistingEntries();
            }

            if (args.Any(a => a == "-removeDupes"))
            {
                DBCommands.RemoveDuplicates();
            }

            if (args.Any(a => a.StartsWith("-createStandalone")))
            {
                //TODO: finish this to take in a parameter thats a relationID (i think, possibly a geo area)
                //Near-future plan: make an app that covers an area and pulls in all data for it.
                //like a university or a park. Draws ALL objects there to an SQLite DB used directly by an app, no data connection.
                //Second arg: area covered somehow (way or relation ID of thing to use? big plus code?)
                //Get a min and a max point, make a box, load all the elements in that from the parent file.

                //TODO: switch this to make a detailed entry for a relation.
                //So i need a second parameter for relationID, then use that to pull the envelope for its bound
                //and output a file with all that data.

                //Test sample covers OSU. OSU is .0307 x .0154 degrees.
                GeoPoint min = new GeoPoint(39.9901, -83.0496);
                GeoPoint max = new GeoPoint(40.0208, -83.0035);
                GeoArea box = new GeoArea(min, max);
                string filename = ParserSettings.PbfFolder + "ohio-latest.osm.pbf";

                int relationId = args.Where(a => a.StartsWith("-createStandalone")).First().Split('|')[1].ToInt();
                CreateStandaloneDB(relationId, filename); //Also generates map tiles here, but doesn't include them in the database directly due to my app. 
            }

            if (args.Any(a => a.StartsWith("-checkFile:")))
            {
                //scan a file for information on what will or won't load.
                string arg = args.Where(a => a.StartsWith("-checkFile:")).First().Replace("-checkFile:", "");
                PbfOperations.ValidateFile(arg);
            }

            if (args.Any(a => a == "-autoCreateMapTiles")) //better for letting the app decide which tiles to create than manually calling out Cell6 names.
            {
                //NOTE: this loop ran at 11 maptiles per second on my original attempt. This optimized setup runs at up to 1300 maptiles per second. I haven't yet tracked down some of the variability and stability issues.
                //They may related to running the DB and this process on the same physical PC.
                //Remember: this shouldn't draw GeneratedMapTile areas, nor should this create them.
                //Tiles should be redrawn when those get made, if they get made.
                //This should also over-write existing map tiles if present, in case the data's been updated since last run.
                //TODO: add logic to either overwrite or skip existing tiles.
                bool skip = true; //This skips over 128,000 tiles in about a minute. Decent.

                //Potential alternative idea:
                //One loops detects which map tiles need drawn, using the algorithm, and saves that list to a new DB table
                //A second process digs through that list and draws the map tiles, then marks them  as drawn (or deletes them from the list?)


                //Search for all areas that needs a map tile created.
                List<string> Cell2s = new List<string>();

                //Cell2 detection loop: 22-CV. All others are 22-XX.
                for (var pos1 = 0; pos1 <= OpenLocationCode.CodeAlphabet.IndexOf('C'); pos1++)
                    for (var pos2 = 0; pos2 <= OpenLocationCode.CodeAlphabet.IndexOf('V'); pos2++)
                    {
                        string cellToCheck = OpenLocationCode.CodeAlphabet[pos1].ToString() + OpenLocationCode.CodeAlphabet[pos2].ToString();
                        var area = new OpenLocationCode(cellToCheck);
                        var tileNeedsMade = DoPlacesExist(area.Decode());
                        if (tileNeedsMade)
                        {
                            Log.WriteLog("Noting: Cell2 " + cellToCheck + " has areas to draw");
                            Cell2s.Add(cellToCheck);
                        }
                        else
                        {
                            Log.WriteLog("Skipping Cell2 " + cellToCheck + " for future mapdrawing checks.");
                        }
                    }

                foreach (var cell2 in Cell2s)
                    DetectMapTilesRecursive(cell2, skip);
            }

            if (args.Any(a => a == "-extractBigAreas"))
            {
                //ExtractAreasFromLargeFile(ParserSettings.PbfFolder + "planet-latest.osm.pbf"); //Guarenteed to have everything. Estimates 103 minutes per relation.
                PbfOperations.ExtractAreasFromLargeFile(ParserSettings.PbfFolder + "north-america-latest.osm.pbf");
            }

            if (args.Any(a => a.StartsWith("-populateEmptyArea:")))
            {
                //NOTE: 86GXVH appears to be entirely empty of interesting features, it's a good spot to start testing if you want to generate lots of areas.
                var db = new PraxisContext();
                var cell6 = args.Where(a => a.StartsWith("-populateEmptyArea:")).First().Split(":")[1];
                CodeArea box6 = OpenLocationCode.DecodeValid(cell6);
                var location6 = Converters.GeoAreaToPolygon(box6);
                var places = db.MapData.Where(md => md.place.Intersects(location6) && md.AreaTypeId < 13).ToList();
                var fakeplaces = db.GeneratedMapData.Where(md => md.place.Intersects(location6)).ToList();

                for (int x = 0; x < 20; x++)
                {
                    for (int y = 0; y < 20; y++)
                    {
                        string cell8 = cell6 + OpenLocationCode.CodeAlphabet[x] + OpenLocationCode.CodeAlphabet[y];
                        CodeArea box = OpenLocationCode.DecodeValid(cell8);
                        var location = Converters.GeoAreaToPolygon(box);
                        if (!places.Any(md => md.place.Intersects(location)) && !fakeplaces.Any(md => md.place.Intersects(location)))
                            CreateInterestingPlaces(cell8);
                    }
                }
            }
            return;
        }

        //TODO: this has an ordering error. The tile code draws them correctly, but they aren't getting put in the right location for some reason. Is this still true?
        public static void DetectMapTilesRecursive(string parentCell, bool skipExisting)
        {
            List<string> cellsFound = new List<string>();
            List<MapTile> tilesGenerated = new List<MapTile>(400); //Might need to be a ConcurrentBag or something similar?
                                                                   //Dictionary<string, string> existingTiles = new Dictionary<string, string>();
                                                                   //if (parentCell.Length == 6 && skipExisting)
                                                                   //{
                                                                   //var db = new PraxisContext();
                                                                   //existingTiles = db.MapTiles.Where(m => m.PlusCode.StartsWith(parentCell)).Select(m => m.PlusCode).ToDictionary<string, string>(k => k);
                                                                   //db.Dispose();
                                                                   //db = null;
                                                                   //}

            if (parentCell.Length == 4)
            {
                var checkProgress = new PraxisContext();
                bool skip = checkProgress.TileTrackings.Any(tt => tt.PlusCodeCompleted == parentCell);
                checkProgress.Dispose();
                checkProgress = null;
                if (skip)
                    return;
            }

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            //ConcurrentBag<MapData>cell6Data = new ConcurrentBag<MapData>();
            List<MapData> cell6Data = new List<MapData>();
            if (parentCell.Length == 6)
            {
                var area = OpenLocationCode.DecodeValid(parentCell);
                var areaPoly = Converters.GeoAreaToPolygon(area);
                var tempPlaces = GetPlaces(area, null, false, false);
                foreach (var t in tempPlaces)
                {
                    t.place = t.place.Intersection(areaPoly);
                    cell6Data.Add(t);
                }

            }

            //This is fairly well optimized, and I suspect there's not much more I can do here to get this to go faster.
            //Between 100 and 1300 map tiles drawn per seconds if all 400 are in a Cell6. Half that if not, suggests there's some overhead in doing the Decode and DoPlacesExist checks that can't really go away.
            //using 2 parallel loops is faster than 1 or 0. Having MariaDB on the same box is what pegs the CPU, not this double-parallel loop.
            System.Threading.Tasks.Parallel.For(0, 20, (pos1) =>
            //for (int pos1 = 0; pos1 < 20; pos1++)
                System.Threading.Tasks.Parallel.For(0, 20, (pos2) =>
                //for (int pos2 = 0; pos2 < 20; pos2++)
                {
                    string cellToCheck = parentCell + OpenLocationCode.CodeAlphabet[pos1].ToString() + OpenLocationCode.CodeAlphabet[pos2].ToString();
                    var area = new OpenLocationCode(cellToCheck).Decode();
                    if (cellToCheck.Length == 8) //We don't want to do the DoPlacesExist check here, since we'll want empty tiles for empty areas at this l
                    {
                        //string exists = "";
                        //if (skipExisting && existingTiles.TryGetValue(cellToCheck, out exists))
                        //{
                        //nothing
                        //Log.WriteLog("Skipping tile draw for " + cellToCheck + ", already exists", Log.VerbosityLevels.High);
                        //}
                        //else
                        //{
                        //Draw this map tile
                        //var places = GetPlaces(area);
                        //var places = GetPlacesCB(area, cell6Data, false);
                        var places = GetPlaces(area, cell6Data, false); //These are cloned in GetPlaces, so we aren't intersecting areas twice and breaking drawing.
                        var tileData = MapTiles.DrawAreaMapTileSkia(ref places, area, 11); //now Skia drawing, should be faster. Peaks around 2600/s. ImageSharp peaked at 1600/s.
                        tilesGenerated.Add(new MapTile() { CreatedOn = DateTime.Now, mode = 1, tileData = tileData, resolutionScale = 11, PlusCode = cellToCheck });
                        Log.WriteLog("Cell " + cellToCheck + " Drawn", Log.VerbosityLevels.High);
                        //}
                    }
                    else
                    {
                        var tileNeedsMade = DoPlacesExist(area);
                        if (tileNeedsMade)
                        {
                            Log.WriteLog("Noting: Cell" + cellToCheck.Length + " " + cellToCheck + " has areas to draw");
                            cellsFound.Add(cellToCheck);
                        }
                        else
                        {
                            Log.WriteLog("Skipping Cell" + cellToCheck.Length + " " + cellToCheck + " for future mapdrawing checks.", Log.VerbosityLevels.High);
                        }
                    }
                })); //add ) here if i do want 2 parallel loops. I might be losing some overhead to managing 400 threads.
            if (tilesGenerated.Count() > 0)
            {
                var db = new PraxisContext();
                db.MapTiles.AddRange(tilesGenerated);
                db.SaveChanges(); //This should run for every Cell6, saving up to 400 per batch.
                db.Dispose(); //connection needs terminated since this is recursive, or we will hit a max connections error eventually.
                db = null;
                Log.WriteLog("Saved records for Cell " + parentCell + " - " + tilesGenerated.Count() + " maptiles drawn and saved in " + sw.Elapsed.ToString());
            }
            foreach (var cellF in cellsFound)
                DetectMapTilesRecursive(cellF, skipExisting);

            if (parentCell.Length == 4)
            {
                var saveProgress = new PraxisContext();
                saveProgress.TileTrackings.Add(new TileTracking() { PlusCodeCompleted = parentCell });
                saveProgress.SaveChanges();
                saveProgress.Dispose();
                saveProgress = null;
                Log.WriteLog("Saved records for Cell4 " + parentCell + " in " + sw.Elapsed.ToString());
            }
        }      

        //mostly-complete function for making files for a standalone app.
        public static void CreateStandaloneDB(long relationID, string parentFile, bool saveToDB = false, bool saveToFolder = true)
        {
            //TODO: add a parmeter to determine if maptiles are saved in the DB or separately in a folder.            
            //TODO in practice:
            //Set up area name to be area type  if name is blank.
            //

            var mainDb = new PraxisContext();
            var sqliteDb = new StandaloneContext(relationID.ToString());// "placeholder";
            sqliteDb.Database.EnsureCreated();

            var fullArea = mainDb.MapData.Where(m => m.RelationId == relationID).FirstOrDefault();
            if (fullArea == null)
                return;

            //Add a Cell8's worth of space to the edges of the area.
            GeoArea buffered = new GeoArea(fullArea.place.EnvelopeInternal.MinY, fullArea.place.EnvelopeInternal.MinX, fullArea.place.EnvelopeInternal.MaxY, fullArea.place.EnvelopeInternal.MaxX);
            //var intersectCheck = Converters.GeoAreaToPreparedPolygon(buffered);
            var intersectCheck = Converters.GeoAreaToPolygon(buffered); //Cant use prepared geometry against the db directly

            var allPlaces = mainDb.MapData.Where(md => intersectCheck.Intersects(md.place)).ToList();

            //now we have the list of places we need to be concerned with. 
            //start drawing maptiles and sorting out data.
            var swCorner = new OpenLocationCode(intersectCheck.EnvelopeInternal.MinY, intersectCheck.EnvelopeInternal.MinX);
            var neCorner = new OpenLocationCode(intersectCheck.EnvelopeInternal.MaxY, intersectCheck.EnvelopeInternal.MaxX);

            System.IO.Directory.CreateDirectory(relationID + "Tiles");
            //now, for every Cell8 involved, draw and name it.
            for (var y = swCorner.Decode().SouthLatitude; y <= neCorner.Decode().NorthLatitude; y += resolutionCell8)
            {
                for (var x = swCorner.Decode().WestLongitude; x <= neCorner.Decode().EastLongitude; x += resolutionCell8)
                {
                    //make map tile.
                    var plusCode = new OpenLocationCode(y, x, 10);
                    var areaForTile = new GeoArea(new GeoPoint(y, x), new GeoPoint(y + resolutionCell8, x + resolutionCell8));
                    var acheck2 = Converters.GeoAreaToPolygon(areaForTile);
                    var areaList = allPlaces.Where(a => a.place.Intersects(acheck2)).Select(a => a.Clone()).ToList();

                    System.Text.StringBuilder terrainInfo = AreaTypeInfo.SearchArea(ref areaForTile, ref areaList, true);
                    var splitData = terrainInfo.ToString().Split(Environment.NewLine);
                    foreach (var sd in splitData)
                    {
                        if (sd == "") //last result is always a blank line
                            continue;


                        var subParts = sd.Split('|'); //PlusCode|Name|AreaTypeID|MapDataID
                                                      //TODO: testing this part, this might be unnecessary if the app handles it
                                                      //if (subParts[1] == "")
                                                      //subParts[1] = areaIdReference[subParts[2].ToInt()].FirstOrDefault();

                        sqliteDb.TerrainInfo.Add(new TerrainInfo() { Name = subParts[1], areaType = subParts[2].ToInt(), PlusCode = subParts[0], MapDataID = subParts[3].ToInt() });
                    }

                    var tile = MapTiles.DrawAreaMapTileSkia(ref areaList, areaForTile, 11);

                    if (saveToDB) //Some apps will prefer a single self-contained file
                        sqliteDb.MapTiles.Add(new MapTileDB() { image = tile, layer = 1, PlusCode = plusCode.CodeDigits.Substring(0,8) });

                    if (saveToFolder) //some apps, like my Solar2D apps, can't use the byte[] and need files.
                        System.IO.File.WriteAllBytes(relationID + "Tiles\\" + plusCode.CodeDigits.Substring(0, 8) + ".pngTile", tile); //Solar2d also can't load pngs directly from an apk file in android, but the rule is extension based.
                }
            }
            sqliteDb.SaveChanges();

            //insert default entries.
            sqliteDb.PlayerStats.Add(new PlayerStats() { timePlayed = 0, distanceWalked = 0 });
            sqliteDb.Bounds.Add(new Bounds() { EastBound = neCorner.Decode().EastLongitude, NorthBound = neCorner.Decode().NorthLatitude, SouthBound = swCorner.Decode().SouthLatitude, WestBound = swCorner.Decode().WestLongitude });
            sqliteDb.SaveChanges();
        }

        public static void SingleTest()
        {
            //trying to find one relation to fix.
            string filename = ParserSettings.PbfFolder + "ohio-latest.osm.pbf";

            List<NodeData> nodes = new List<NodeData>();
            List<WayData> ways = new List<WayData>();
            List<MapData> processedEntries = new List<MapData>();
            //Minimizes time spend boosting capacity and copying the internal values later.
            nodes.Capacity = 100000;
            ways.Capacity = 100000;
            processedEntries.Capacity = 100000;

            long oneId = 6113131;

            FileStream fs = new FileStream(filename, FileMode.Open);
            var source = new PBFOsmStreamSource(fs);
            var relation = source.Where(s => s.Type == OsmGeoType.Relation && s.Id == oneId).Select(s => (OsmSharp.Relation)s).ToList(); //should be a list of 1
            var referencedWays = relation.SelectMany(r => r.Members.Where(m => m.Type == OsmGeoType.Way).Select(m => m.Id)).Distinct().ToLookup(k => k, v => v);
            var ways2 = source.Where(s => s.Type == OsmGeoType.Way && referencedWays[s.Id.Value].Count() > 0).Select(s => (OsmSharp.Way)s).ToList();
            var referencedNodes = ways2.SelectMany(m => m.Nodes).Distinct().ToLookup(k => k, v => v);
            var nodes2 = source.Where(s => s.Type == OsmGeoType.Node && referencedNodes[s.Id.Value].Count() > 0).Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetPlaceName(n.Tags), GetPlaceType(n.Tags))).ToList();
            Log.WriteLog("Relevant data pulled from file at" + DateTime.Now);

            //Log.WriteLog("Creating node lookup for " + osmNodes.Count() + " nodes"); //33 million nodes across 2 million ways will tank this app at 16GB RAM
            var osmNodeLookup = nodes2.AsParallel().ToLookup(k => k.Id, v => v);
            Log.WriteLog("Found " + osmNodeLookup.Count() + " unique nodes");

            //Write nodes as mapdata if they're tagged separately from other things.
            Log.WriteLog("Finding tagged nodes at " + DateTime.Now);
            var taggedNodes = nodes2.AsParallel().Where(n => n.name != "" && n.type != "").ToList();
            processedEntries.AddRange(taggedNodes.Select(s => Converters.ConvertNodeToMapData(s)));
            taggedNodes = null;

            //This is now the slowest part of the processing function.
            Log.WriteLog("Converting " + ways2.Count() + " OsmWays to my Ways at " + DateTime.Now);
            ways.Capacity = ways2.Count();
            ways = ways2.AsParallel().Select(w => new WayData()
            {
                id = w.Id.Value,
                name = GetPlaceName(w.Tags),
                AreaType = GetPlaceType(w.Tags),
                nodRefs = w.Nodes.ToList()
            })
            .ToList();
            ways2 = null; //free up RAM we won't use again.
            Log.WriteLog("List created at " + DateTime.Now);

            int wayCounter = 0;
            //foreach (WayData w in ways)
            System.Threading.Tasks.Parallel.ForEach(ways, (w) =>
            {
                wayCounter++;
                if (wayCounter % 10000 == 0)
                    Log.WriteLog(wayCounter + " processed so far");

                PbfOperations.LoadNodesIntoWay(ref w, ref osmNodeLookup);
            });

            Log.WriteLog("Ways populated with Nodes at " + DateTime.Now);
            nodes2 = null; //done with these now, can free up RAM again.

            processedEntries.AddRange(PbfOperations.ProcessRelations(ref relation, ref ways));

            processedEntries.AddRange(ways.Select(w => Converters.ConvertWayToMapData(ref w)));
            ways = null;

            string destFileName = System.IO.Path.GetFileNameWithoutExtension(filename);
            FileCommands.WriteMapDataToFile(ParserSettings.JsonMapDataFolder + destFileName + "-MapData-Test.json", ref processedEntries);
            DBCommands.AddMapDataToDBFromFiles();

        }

        public static void DownloadPbfFile(string topLevel, string subLevel1, string subLevel2, string destinationFolder)
        {
            //pull a fresh copy of a file from geofabrik.de (or other mirror potentially)
            //save it to the same folder as configured for pbf files (might be passed in)
            //web paths http://download.geofabrik.de/north-america/us/ohio-latest.osm.pbf
            //root, then each parent division. Starting with USA isn't too hard.
            //topLevel = "north-america";
            //subLevel1 = "us";
            //subLevel2 = "ohio";
            var wc = new WebClient();
            wc.DownloadFile("http://download.geofabrik.de/" + topLevel + "/" + subLevel1 + "/" + subLevel2 + "-latest.osm.pbf", destinationFolder + subLevel2 + "-latest.osm.pbf");
        }

        /* For reference: the tags Pokemon Go appears to be using. I don't need all of these. I have a few it doesn't, as well.
         * POkemon Go is using these as map tiles, not just content. This is not primarily a maptile app.
    KIND_BASIN
    KIND_CANAL
    KIND_CEMETERY - Have
    KIND_CINEMA
    KIND_COLLEGE - Have
    KIND_COMMERCIAL
    KIND_COMMON
    KIND_DAM
    KIND_DITCH
    KIND_DOCK
    KIND_DRAIN
    KIND_FARM
    KIND_FARMLAND
    KIND_FARMYARD
    KIND_FOOTWAY -Have
    KIND_FOREST
    KIND_GARDEN
    KIND_GLACIER
    KIND_GOLF_COURSE
    KIND_GRASS
    KIND_HIGHWAY -have
    KIND_HOSPITAL
    KIND_HOTEL
    KIND_INDUSTRIAL
    KIND_LAKE -have, as water
    KIND_LAND
    KIND_LIBRARY
    KIND_MAJOR_ROAD - have
    KIND_MEADOW
    KIND_MINOR_ROAD - have
    KIND_NATURE_RESERVE - Have
    KIND_OCEAN - have, as water
    KIND_PARK - Have
    KIND_PARKING - have
    KIND_PATH - have, as trail
    KIND_PEDESTRIAN
    KIND_PITCH
    KIND_PLACE_OF_WORSHIP
    KIND_PLAYA
    KIND_PLAYGROUND
    KIND_QUARRY
    KIND_RAILWAY
    KIND_RECREATION_AREA
    KIND_RESERVOIR
    KIND_RETAIL - Have
    KIND_RIVER - have, as water
    KIND_RIVERBANK - have, as water
    KIND_RUNWAY
    KIND_SCHOOL
    KIND_SPORTS_CENTER
    KIND_STADIUM
    KIND_STREAM - have, as water
    KIND_TAXIWAY
    KIND_THEATRE
    KIND_UNIVERSITY - Have
    KIND_URBAN_AREA
    KIND_WATER - Have
    KIND_WETLAND - Have
    KIND_WOOD
         */

        /*
         * and for reference, the Google Maps Playable Locations valid types (Interaction points, not terrain types?)
         *  education
            entertainment
            finance
            food_and_drink
            outdoor_recreation
            retail
            tourism
            transit
            transportation_infrastructure
            wellness
         */
    }
}
