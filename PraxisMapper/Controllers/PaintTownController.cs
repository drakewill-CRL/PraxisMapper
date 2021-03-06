﻿using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CoreComponents;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using static CoreComponents.DbTables;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class PaintTownController : Controller
    {
        private readonly IConfiguration Configuration;
        private static MemoryCache cache; //PaintTown is meant to be a rapidly-changing game mode, so it won't cache a lot of data in RAM.
        public static bool isResetting = false;
        //PaintTown is a simplified version of AreaControl.
        //1) It operates on a per-Cell basis instead of a per-MapData entry basis.
        //2) The 'earn points to spend points' part is removed in favor of auto-claiming areas you walk into. (A lockout timer is applied to stop 2 people from constantly flipping one area ever half-second)
        //3) No direct interaction with the device is required. Game needs to be open, thats all.
        //The leaderboards for PaintTown reset regularly (weekly? Hourly? ), and could be set to reset very quickly and restart (3 minutes of gameplay, 1 paused for reset). 
        //Default config is a weekly run Sat-Fri, with a 30 second lockout on cells.
        //TODO: allow pre-configured teams for specific events? this is difficult if I don't want to track users on the server, since this would have to be by deviceID
        //TODO: allow an option to let you choose to join a team. Yes, i wanted to avoid this. Yes, there's still good cases for it.

        public PaintTownController(IConfiguration configuration)
        {
            try
            {
                if (isResetting)
                    return;

                Classes.PerformanceTracker pt = new Classes.PerformanceTracker("PaintTownConstructor");
                var db = new PraxisContext();
                Configuration = configuration;
                if (cache == null && Configuration.GetValue<bool>("enableCaching") == true)
                {
                    var options = new MemoryCacheOptions();
                    options.SizeLimit = 1024;
                    cache = new MemoryCache(options);

                    //fill the cache with some early useful data
                    Dictionary<int, DateTime> resetTimes = new Dictionary<int, DateTime>();
                    foreach (var i in db.PaintTownConfigs)
                    {
                        if (i.Repeating)
                            resetTimes.Add(i.PaintTownConfigId, i.NextReset);
                    }
                    MemoryCacheEntryOptions mco = new MemoryCacheEntryOptions() { Size = 1 };
                    cache.Set("resetTimes", resetTimes, mco);

                    List<long> teams = db.Factions.Select(f => f.FactionId).ToList();
                    cache.Set("factions", teams, mco);

                    var settings = db.ServerSettings.FirstOrDefault();
                    cache.Set("settings", settings, mco);
                }

                if (cache != null)
                {
                    Dictionary<int, DateTime> cachedTimes = null;
                    if (cache.TryGetValue("resetTimes", out cachedTimes))
                    foreach (var t in cachedTimes)
                        if (t.Value < DateTime.Now)
                            CheckForReset(t.Key);
                }
                else
                {
                    var instances = db.PaintTownConfigs.ToList();
                    foreach (var i in instances)
                        if (i.Repeating) //Don't check this on non-repeating instances.
                            CheckForReset(i.PaintTownConfigId); //Do this on every call so we don't have to have an external app handle these, and we don't miss one.
                }
                pt.Stop();
            }
            catch (Exception ex)
            {
                Classes.ErrorLogger.LogError(ex);
            }
        }

        [HttpGet]
        [Route("/[controller]/LearnCell8/{instanceID}/{Cell8}")]
        public string LearnCell8(int instanceID, string Cell8)
        {
            try
            {
                Classes.PerformanceTracker pt = new Classes.PerformanceTracker("LearnCell8PaintTown");
                var cellData = PaintTown.LearnCell8(instanceID, Cell8);
                string results = "";
                foreach (var cell in cellData)
                    results += cell.Cell10 + "=" + cell.FactionId + "|";
                return results;
            }
            catch (Exception ex)
            {
                Classes.ErrorLogger.LogError(ex);
                return "";
            }
        }

        [HttpGet]
        [Route("/[controller]/ClaimCell10/{factionId}/{Cell10}")]
        public int ClaimCell10(int factionId, string Cell10)
        {
            try
            {
                Classes.PerformanceTracker pt = new Classes.PerformanceTracker("ClaimCell10PaintTown");
                //Mark this cell10 as belonging to this faction, update the lockout timer.
                var db = new PraxisContext();
                //run all the instances at once.
                List<long> factions = null;
                ServerSetting settings = null;
                if (cache != null)
                {
                    factions = (List<long>)cache.Get("factions");
                    settings = (ServerSetting)cache.Get("settings");
                }
                
                if (factions == null)
                    factions = db.Factions.Select(f => f.FactionId).ToList();

                if (!factions.Any(f => f == factionId))
                {
                    pt.Stop("NoFaction:" + factionId);
                    return 0; //We got a claim for an invalid team, don't save anything.
                }

                //check boundaries
                if (settings == null)
                    settings = db.ServerSettings.FirstOrDefault();

                if (!Place.IsInBounds(Cell10, settings))
                {
                    pt.Stop("OOB:" + Cell10);
                    return 0;
                }
                int claimed = 0;

                //User will get a point if any of the configs get flipped from this claim.
                foreach (var config in db.PaintTownConfigs.Where(t => t.Repeating || (t.StartTime < DateTime.Now && t.NextReset > DateTime.Now)).ToList())
                {
                    var entry = db.PaintTownEntries.Where(t => t.PaintTownConfigId == config.PaintTownConfigId && t.Cell10 == Cell10).FirstOrDefault();
                    if (entry == null)
                    {
                        entry = new DbTables.PaintTownEntry() { Cell10 = Cell10, PaintTownConfigId = config.PaintTownConfigId, Cell8 = Cell10.Substring(0, 8), CanFlipFactionAt = DateTime.Now.AddSeconds(-1) };
                        db.PaintTownEntries.Add(entry);
                    }
                    if (DateTime.Now > entry.CanFlipFactionAt)
                    {
                        if (entry.FactionId != factionId)
                        {
                            claimed = 1;
                            entry.FactionId = factionId;
                            entry.ClaimedAt = DateTime.Now;
                        }
                        entry.CanFlipFactionAt = DateTime.Now.AddSeconds(config.Cell10LockoutTimer);
                    }
                }
                db.SaveChanges();
                pt.Stop(Cell10 + claimed);
                return claimed;
            }
            catch(Exception ex)
            {
                Classes.ErrorLogger.LogError(ex);
                return 0;
            }
        }

        [HttpGet]
        [Route("/[controller]/Scoreboard/{instanceID}")]
        public string Scoreboard(int instanceID)
        {
            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("ScoreboardPaintTown");
            //which faction has the most cell10s?
            //also, report time, primarily for recordkeeping 
            var db = new PraxisContext();
            var teams = db.Factions.ToLookup(k => k.FactionId, v => v.Name);
            var data = db.PaintTownEntries.Where(t => t.PaintTownConfigId == instanceID).GroupBy(g => g.FactionId).Select(t => new { instanceID = instanceID, team = t.Key, score = t.Count() }).OrderByDescending(t => t.score).ToList();
            var modeName = db.PaintTownConfigs.Where(t => t.PaintTownConfigId == instanceID).FirstOrDefault().Name;
            //TODO: data to string of some kind.
            string results = modeName + "#" + DateTime.Now + "|";
            foreach (var d in data)
            {
                results += teams[d.team].FirstOrDefault() + "=" + d.score + "|";
            }
            pt.Stop(instanceID.ToString());
            return results;
        }

        [HttpGet] //TODO this is a POST? TODO take in an instanceID?
        [Route("/[controller]/PastScoreboards")]
        public static string PastScoreboards()
        {
            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("PastScoreboards");
            var db = new PraxisContext();
            var results = db.PaintTownScoreRecords.OrderByDescending(t => t.RecordedAt).ToList();
            //Scoreboard already uses # and | as separators, we will use \n now.
            var parsedResults = String.Join("\n", results);
            pt.Stop();
            return parsedResults;
        }

        [HttpGet] //TODO this is a POST?
        [Route("/[controller]/ModeTime/{instanceID}")]
        public TimeSpan ModeTime(int instanceID)
        {
            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("ModeTime");
            //how much time remains in the current session. Might be 3 minute rounds, might be week long rounds.
            var db = new PraxisContext();
            var time = db.PaintTownConfigs.Where(t => t.PaintTownConfigId == instanceID).Select(t => t.NextReset).FirstOrDefault();
            pt.Stop(instanceID.ToString());
            return DateTime.Now - time;
        }

        public void ResetGame(int instanceID, bool manaulReset = false, DateTime? nextEndTime = null)
        {
            //It's possible that this function might be best served being an external console app.
            //Doing the best I can here to set it up to make that unnecessary, but it may not be enough.
            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("ResetGame");
            if (isResetting)
                return;

            isResetting = true;
            //TODO: determine if any of these commands needs to be raw SQL for performance reasons.
            //Clear out any stored data and fire the game mode off again.
            //Fire off a reset.
            var db = new PraxisContext();
            var twConfig = db.PaintTownConfigs.Where(t => t.PaintTownConfigId == instanceID).FirstOrDefault();
            var nextTime = twConfig.NextReset.AddHours(twConfig.DurationHours);
            if (manaulReset && nextEndTime.HasValue)
                nextTime = nextEndTime.Value;
            twConfig.NextReset = nextTime;

            db.PaintTownEntries.RemoveRange(db.PaintTownEntries.Where(tw => tw.PaintTownConfigId == instanceID));
            //db.PaintTownTeamAssignments.RemoveRange(db.PaintTownTeamAssignments.Where(ta => ta.PaintTownConfigId == instanceID)); //This might be better suited to raw SQL. TODO investigate

            //create dummy entries so team assignments works faster
            //foreach (var faction in db.Factions)
                //db.PaintTownTeamAssignments.Add(new DbTables.PaintTownTeamAssignment() { deviceID = "dummy", FactionId = (int)faction.FactionId, PaintTownConfigId = instanceID, ExpiresAt = nextTime });

            //record score results.
            var score = new DbTables.PaintTownScoreRecord();
            score.Results = Scoreboard(instanceID);
            score.RecordedAt = DateTime.Now;
            score.PaintTownConfigId = instanceID;
            db.PaintTownScoreRecords.Add(score);
            db.SaveChanges();
            isResetting = false;
            pt.Stop(instanceID.ToString());
        }

        [HttpGet] //TODO this is a POST? TODO take in an instanceID?
        [Route("/[controller]/GetEndDate/{instanceID}")]
        public string GetEndDate(int instanceID)
        {
            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("GetEndDate");
            var db = new PraxisContext();
            var twConfig = db.PaintTownConfigs.Where(t => t.PaintTownConfigId == instanceID).FirstOrDefault();
            var results = twConfig.NextReset.ToString();
            pt.Stop();
            return results;
        }

        public void CheckForReset(int instanceID)
        {
            if (isResetting)
                return;

            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("CheckForReset");

            //TODO: cache these results into memory so I can skip a DB lookup every single call.
            var db = new PraxisContext();
            var twConfig = db.PaintTownConfigs.Where(t => t.PaintTownConfigId == instanceID).FirstOrDefault();
            if (twConfig.DurationHours == -1) //This is a permanent instance.
                return;

            if (DateTime.Now > twConfig.NextReset)
                ResetGame(instanceID);
            pt.Stop();
        }

        [HttpGet]
        [Route("/[controller]/GetInstances/")]
        public string GetInstances()
        {
            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("GetInstances");

            var db = new PraxisContext();
            var instances = db.PaintTownConfigs.ToList();
            string results = "";
            foreach (var i in instances)
            {
                results += i.PaintTownConfigId + "|" + i.NextReset.ToString() + "|" + i.Name + Environment.NewLine;
            }
            pt.Stop();
            return results;
        }

        [HttpGet]
        [Route("/[controller]/ManualReset/{instanceID}")]
        public string ManualReset(int instanceID)
        {
            //TODO: should be an admin command and require hte password.
            ResetGame(instanceID, true);
            return "OK";
        }

        //public PaintTownTeamAssignment GetTeamAssignment(string deviceID, int instanceID)
        //{
        //    var db = new PraxisContext();
        //    var r = new Random();
        //    var teamEntry = db.PaintTownTeamAssignments.Where(ta => ta.deviceID == deviceID && ta.PaintTownConfigId == instanceID).FirstOrDefault();
        //    if (teamEntry == null)
        //    {
        //        var config = db.PaintTownConfigs.Where(c => c.PaintTownConfigId == instanceID).FirstOrDefault();
        //        teamEntry = new DbTables.PaintTownTeamAssignment();
        //        teamEntry.deviceID = deviceID;
        //        teamEntry.PaintTownConfigId = instanceID;
        //        teamEntry.ExpiresAt = DateTime.Now.AddDays(-1); //entry exists, immediately is expired. This is used to identify a newly created entry.
        //        teamEntry.FactionId = r.Next(0, db.Factions.Count()) + 1; //purely randomly assigned.
        //        db.PaintTownTeamAssignments.Add(teamEntry);
        //        db.SaveChanges();
        //    }
        //    return teamEntry;
        //}
    }
}
