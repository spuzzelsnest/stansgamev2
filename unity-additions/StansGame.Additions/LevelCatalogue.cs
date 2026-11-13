namespace StansGame.Additions
{
    /// <summary>One entry in the level select.</summary>
    public sealed class LevelInfo
    {
        public string Id;          // stable key in the save file
        public int Number;
        public string Title;
        public string Place;
        public string Date;
        public string Summary;
        public bool Available;     // false: shown as "in development", cannot be started
        public bool Coop;
        public bool Training;      // the bootcamp: always open, never locks or unlocks a level
    }

    /// <summary>
    /// The levels shown in the level select, in order, the bootcamp first. Level N+1 unlocks when level N
    /// is completed; the bootcamp is always open and plays no part in unlocking.
    /// Once the Unity source is in, this list belongs in a ScriptableObject asset (see the game
    /// structure plan, R6), so a new level is one new entry and no code change.
    /// </summary>
    public static class LevelCatalogue
    {
        public static readonly LevelInfo[] All =
        {
            new LevelInfo
            {
                Id = Bootcamp.Id,
                Number = 0,
                Title = "Bootcamp",
                Place = "Countess Wear, Exeter",
                Date = "Spring 1944",
                Summary = "D Company rehearsed the bridges here before Normandy. Five minutes, nine stations, every control of the game: move, climb, crawl, swim, shoot, throw, the PIAT, the field dressing and the wireless.",
                Available = true,
                Coop = false,
                Training = true,
            },
            new LevelInfo
            {
                Id = "pegasus-bridge",
                Number = 1,
                Title = "Pegasus Bridge",
                Place = "Bénouville, Normandy",
                Date = "6 June 1944, 00:16",
                Summary = "Operation Deadstick. D Company, 2nd Ox & Bucks Light Infantry, lands by glider next to the canal bridge. Take it intact and hold it until the commandos arrive.",
                Available = true,
                Coop = true,
            },
            new LevelInfo
            {
                Id = "level-2",
                Number = 2,
                Title = "Next mission",
                Place = "In development",
                Date = "",
                Summary = "Joan is building this mission. It appears here once it is in the game.",
                Available = false,
                Coop = true,
            },
        };

        /// <summary>The bootcamp is always open, the first mission too; every next mission opens when the one before is completed.</summary>
        public static bool IsUnlocked(int index)
        {
            if (index < 0 || index >= All.Length || !All[index].Available) return false;
            if (All[index].Training) return true;
            for (int i = index - 1; i >= 0; i--)
                if (!All[i].Training) return Progress.Get(All[i].Id).completed;
            return true;
        }

        /// <summary>The first open mission that is not completed yet, or the last open one when all are done. Never the bootcamp.</summary>
        public static int ContinueIndex()
        {
            int last = IndexOf("pegasus-bridge");
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Training) continue;
                if (!IsUnlocked(i)) break;
                last = i;
                if (!Progress.Get(All[i].Id).completed) return i;
            }
            return last;
        }

        /// <summary>Index of the bootcamp entry.</summary>
        public static int BootcampIndex()
        {
            return IndexOf(Bootcamp.Id);
        }

        /// <summary>Index of the entry with this id, or 0 when there is none.</summary>
        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return i;
            return 0;
        }
    }
}
