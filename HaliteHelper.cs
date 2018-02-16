using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;

/// <summary>
/// Helpful for debugging.
/// </summary>
public static class Log
{
    private static string _logPath;

    /// <summary>
    /// File must exist
    /// </summary>
    public static void Setup(string logPath) {
        _logPath = logPath;
    }

    public static void Information(string message) {
        if (!string.IsNullOrEmpty(_logPath))
            File.AppendAllLines(_logPath, new[] {string.Format("{0}: {1}", DateTime.Now.ToShortTimeString(), message)});
    }
    
    public static void Error(Exception exception) {
        Log.Information(string.Format("ERROR: {0} {1}", exception.Message, exception.StackTrace));
    }
}

public static class Networking
{
    private static string ReadNextLine() {
        var str = Console.ReadLine();
        if (str == null) throw new ApplicationException("Could not read next line from stdin");
        return str;
    }

    private static void SendString(string str) {
        Console.WriteLine(str);
    }

    /// <summary>
    /// Call once at the start of a game to load the map and player tag from the first four stdin lines.
    /// </summary>
    public static Map getInit(out ushort playerTag) {

        // Line 1: Player tag
        if (!ushort.TryParse(ReadNextLine(), out playerTag))
            throw new ApplicationException("Could not get player tag from stdin during init");

        // Lines 2-4: Map
        var map = Map.ParseMap(ReadNextLine(), ReadNextLine(), ReadNextLine());
        return map;
    }

    /// <summary>
    /// Call every frame to update the map to the next one provided by the environment.
    /// </summary>
    public static void getFrame(ref Map map) {
        map.Update(ReadNextLine());
    }


    /// <summary>
    /// Call to acknowledge the initail game map and start the game.
    /// </summary>
    public static void SendInit(string botName) {
        SendString(botName);
    }

    /// <summary>
    /// Call to send your move orders and complete your turn.
    /// </summary>
    public static void SendMoves(IEnumerable<Move> moves) {
        SendString(Move.MovesToString(moves));
    }
}

public enum Direction
{
    Still = 0,
    North = 1,
    East = 2,
    South = 3,
    West = 4
}

public struct Site
{
    public ushort Owner { get; internal set; }
    public ushort Strength { get; internal set; }
    public ushort Production { get; internal set; }
}

public struct Location
{
    public ushort X;
    public ushort Y;
}

public struct Move
{
    public Location Location;
    public Direction Direction;

    internal static string MovesToString(IEnumerable<Move> moves) {
        return string.Join(" ", moves.Select(m => string.Format("{0} {1} {2}", m.Location.X, m.Location.Y, (int)m.Direction)));
    }
}

/// <summary>
/// State of the game at every turn. Use <see cref="GetInitialMap"/> to get the map for a new game from
/// stdin, and use <see cref="NextTurn"/> to update the map after orders for a turn have been executed.
/// </summary>
public class Map
{
    public void Update(string gameMapStr) {
        var gameMapValues = new Queue<string>(gameMapStr.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries));

        ushort x = 0, y = 0;
        while (y < Height) {
            ushort counter, owner;
            if (!ushort.TryParse(gameMapValues.Dequeue(), out counter))
                throw new ApplicationException("Could not get some counter from stdin");
            if (!ushort.TryParse(gameMapValues.Dequeue(), out owner))
                throw new ApplicationException("Could not get some owner from stdin");
            while (counter > 0) {
                _sites[x, y].Owner = owner;
                x++;
                if (x == Width) {
                    x = 0;
                    y++;
                }
                counter--;
            }
        }

        var strengthValues = gameMapValues; // Referencing same queue, but using a name that is more clear
        for (y = 0; y < Height; y++) {
            for (x = 0; x < Width; x++) {
                ushort strength;
                if (!ushort.TryParse(strengthValues.Dequeue(), out strength))
                    throw new ApplicationException("Could not get some strength value from stdin");
                _sites[x, y].Strength = strength;
            }
        }
    }

    /// <summary>
    /// Get a read-only structure representing the current state of the site at the supplied coordinates.
    /// </summary>
    public Site this[ushort x, ushort y] {
        get {
            if (x >= Width)
                throw new IndexOutOfRangeException(string.Format("Cannot get site at ({0},{1}) beacuse width is only {2}", x, y, Width));
            if (y >= Height)
                throw new IndexOutOfRangeException(string.Format("Cannot get site at ({0},{1}) beacuse height is only {2}", x, y, Height));
            return _sites[x, y];
        }
    }

    /// <summary>
    /// Get a read-only structure representing the current state of the site at the supplied location.
    /// </summary>
    public Site this[Location location] => this[location.X, location.Y];

    /// <summary>
    /// Returns the width of the map.
    /// </summary>
    public ushort Width => (ushort)_sites.GetLength(0);

    /// <summary>
    ///  Returns the height of the map.
    /// </summary>
    public ushort Height => (ushort)_sites.GetLength(1);


    public ushort CalcDistance(ushort x1, ushort y1, ushort x2, ushort y2)
    {
        ushort dx = (ushort)Math.Abs(x2 - x1);
        ushort dy = (ushort)Math.Abs(y2 - y1);

        if (dx > (Width / 2))
        {
            dx = (ushort)(Width - dx);
        }
        if (dy > (Height / 2))
        {
            dy = (ushort)(Height - dy);
        }

        return (ushort)(dx + dy);
    }

    // returns angle in degrees (angle is the same as the unit circle in trig)
    // this means 0 degrees is east, 90 degrees is north, 180 degrees is west....
    // x1 and y1 is your current position, x2 and y2 is the ref position
    public short CalcAngle(ushort x1, ushort y1, ushort x2, ushort y2)
    {
        short dx = (short)(x2 - x1);
        short dy = (short)(y2 - y1);

        if (dx > Width - dx)
        {
            dx -= (short)Width;
        }
        else if (-dx > Width + dx)
        {
            dx += (short)Width;
        }

        if (dy > Height - dy)
        {
            dy -= (short)Height;
        }
        else if (-dy > Height + dy)
        {
            dy += (short)Height;
        }

        // convert from to degrees
        return (short) (Math.Atan2(-dy, dx) * (180 / Math.PI));
    }


    public List<Direction> FindNeighboringEnemies(short x, short y, ushort myID)
    {
        short[,] SearchAround = new short[4, 2] { { 0, -1 }, { 1, 0 }, { 0, 1 }, { -1, 0 } };
        //                                          NORTH      EAST     SOUTH      WEST            

        List<Direction> Enemies = new List<Direction>();

        for (int i = 0; i < 4; i++)
        {
            Location SearchLoc = WrapCoord((short)(x + SearchAround[i, 0]), (short)(y + SearchAround[i, 1]));
            if (this[SearchLoc.X, SearchLoc.Y].Owner != myID && this[SearchLoc.X, SearchLoc.Y].Owner != 0)
            {
                Enemies.Add((Direction)(i+1));
            }
        }

        return Enemies;

    }


    public List<Direction> CalcPerimeterDir(short x, short y, ushort myID)
    {
        short[,] SearchAround = new short[4, 2] { { 0, -1 }, { 1, 0 }, { 0, 1 }, { -1, 0 } };
        //                                          NORTH      EAST     SOUTH      WEST            

        List<Direction> PerimeterDir = new List<Direction>();

        for (int i = 0; i < 4; i++)
        {
            Location SearchLoc = WrapCoord((short)(x + SearchAround[i, 0]), (short)(y + SearchAround[i, 1]));
            if (this[SearchLoc.X, SearchLoc.Y].Owner != myID)
            {
                PerimeterDir.Add((Direction)(i+1));
            }
        }

        return PerimeterDir;

    }


    public Location WrapCoord(short x, short y)
    {
        if (x >= Width)
        {
            x -= (short)Width;
        }
        else if (x < 0)
        {
            x += (short)Width;
        }

        if (y >= Height)
        {
            y -= (short)Height;
        }
        else if (y < 0)
        {
            y += (short)Height;
        }

        return new Location { X = (ushort)x, Y = (ushort)y };
    }


    public ushort AverageNeighborStrengths(short x, short y, ushort myID)
    {
        short[,] SearchAround = new short[4, 2] { { 0, -1 }, { 1, 0 }, { 0, 1 }, { -1, 0 } };
        //                                          NORTH      EAST     SOUTH      WEST    

        ushort CumulativeStrengths = 0;
        ushort NumOfStrengths = 1;

        Location currLoc = WrapCoord(x, y);


        x = (short)currLoc.X;
        y = (short)currLoc.Y;


        CumulativeStrengths = this[(ushort)x, (ushort)y].Strength;

        for (int i = 0; i < 4; i++)
        {
            Location temp = WrapCoord((short)(x + SearchAround[i, 0]), (short)(y + SearchAround[i, 1]));

            if (this[temp.X, temp.Y].Owner != myID)
            {
                CumulativeStrengths += this[temp.X, temp.Y].Strength;
                NumOfStrengths++;
            }
        }

        return (ushort)(CumulativeStrengths / NumOfStrengths);
    }


    public Location GetLoc(ushort x, ushort y, Direction SearchDirection)
    {

        short[,] SearchAround = new short[4, 2] { { 0, -1 }, { 1, 0 }, { 0, 1 }, { -1, 0 } };
        //                                          NORTH      EAST     SOUTH      WEST      

        if (SearchDirection == Direction.Still)
        {
            return new Location { X = x, Y = y };
        }


        return WrapCoord((short)(x + SearchAround[(int)(SearchDirection - 1), 0]), (short)(y + SearchAround[(int)(SearchDirection - 1), 1]));
    }




    public Direction FindAttackableEnemies(short x, short y, ushort myID)
    {
        short[,] SearchAround = new short[4, 2] { { 0, -1 }, { 1, 0 }, { 0, 1 }, { -1, 0 } };
        //                                          NORTH      EAST     SOUTH      WEST   

        ushort MostEnemyStrengthCount = 0;

        Direction GoDirection = Direction.Still;

        for (int i = 0; i < 4; i++)
        {
            ushort EnemyStrengthCount = 0;
            Location SearchNeighbor = WrapCoord((short)(x + SearchAround[i, 0]), (short)(y + SearchAround[i, 1]));

            if (this[SearchNeighbor.X, SearchNeighbor.Y].Owner != 0 && this[SearchNeighbor.X, SearchNeighbor.Y].Owner != myID)
            {
                EnemyStrengthCount++;
            }

            for (int j = 0; j < 4; j++)
            {
                Location SearchEnemy = WrapCoord((short)(SearchNeighbor.X + SearchAround[j, 0]), (short)(SearchNeighbor.Y + SearchAround[j, 1]));

                if (this[SearchEnemy.X, SearchEnemy.Y].Owner != 0 && this[SearchEnemy.X, SearchEnemy.Y].Owner != myID)
                {
                    EnemyStrengthCount++;
                }
            }

            if (EnemyStrengthCount > MostEnemyStrengthCount)
            {
                MostEnemyStrengthCount = EnemyStrengthCount;
                GoDirection = (Direction)(i + 1);
            }
        }

        return GoDirection;
    }




    public Direction FindAttackableEnemiesProdMod(short x, short y, ushort myID)
    {
        short[,] SearchAround = new short[4, 2] { { 0, -1 }, { 1, 0 }, { 0, 1 }, { -1, 0 } };
        //                                          NORTH      EAST     SOUTH      WEST   

        Location currLoc = WrapCoord(x, y);
        ushort MostEnemyStrengthCount = 0;
        List<Direction> ViableAttackDirections = new List<Direction>();
        Direction GoDirection = Direction.Still;

        for (int i = 0; i < 4; i++)
        {
            ushort EnemyStrengthCount = 0;
            Location SearchNeighbor = WrapCoord((short)(x + SearchAround[i, 0]), (short)(y + SearchAround[i, 1]));

            if (this[SearchNeighbor.X, SearchNeighbor.Y].Owner != 0 && this[SearchNeighbor.X, SearchNeighbor.Y].Owner != myID)
            {
                EnemyStrengthCount++;
            }

            for (int j = 0; j < 4; j++)
            {
                Location SearchEnemy = WrapCoord((short)(SearchNeighbor.X + SearchAround[j, 0]), (short)(SearchNeighbor.Y + SearchAround[j, 1]));

                if (this[SearchEnemy.X, SearchEnemy.Y].Owner != 0 && this[SearchEnemy.X, SearchEnemy.Y].Owner != myID)
                {
                    EnemyStrengthCount++;
                }
            }

            if (EnemyStrengthCount > MostEnemyStrengthCount)
            {
                ViableAttackDirections.Clear();
                MostEnemyStrengthCount = EnemyStrengthCount;
                ViableAttackDirections.Add((Direction)(i + 1));
            }
            else if (EnemyStrengthCount == MostEnemyStrengthCount)
            {
                ViableAttackDirections.Add((Direction)(i + 1));
            }
        }

        short ProductionCount = -1;
        foreach (Direction direction in ViableAttackDirections)
        {
            Location tempLoc = GetLoc(currLoc.X, currLoc.Y, direction);

            if (this[tempLoc.X, tempLoc.Y].Production > ProductionCount)
            {
                GoDirection = direction;
                ProductionCount = (short)this[tempLoc.X, tempLoc.Y].Production;
            }
        }

        return GoDirection;
    }



    public float GetAggroLevel(ushort myID)
    {
        for (ushort x = 0; x < Width; x++)
        {
            for (ushort y = 0; y < Height; y++)
            {
                // If the unit is mine check around it to find enemy neighbors and set aggro level based on it
                if (this[x, y].Owner == myID)
                {
                    if (FindAttackableEnemies((short)x, (short)y, myID) != Direction.Still)
                    {
                        return (float)0.83;
                    }
                }
            }
        }

        return (float)0.01;
    }






    public float DeterminePotential(short x, short y, ushort myID)
    {
        short[,] SearchAround = new short[4, 2] { { 0, -1 }, { 1, 0 }, { 0, 1 }, { -1, 0 } }; //  {-1,-1}, { 1, 1} , { -1, 1}, { 1, -1} };
        //                                          NORTH      EAST     SOUTH      WEST       NW        SE        SW       NE

        float Potential = 0;
        ushort neighborCount = 0;

        Location temp = WrapCoord(x, y);

        if (this[temp.X, temp.Y].Owner != myID)
        {
            Potential += this[temp.X, temp.Y].Strength / (this[temp.X, temp.Y].Production + 1);
            neighborCount++;
        }

        for (int i = 0; i < 4; i++)
        {
            Location SearchNeighbor = WrapCoord((short)(x + SearchAround[i, 0]), (short)(y + SearchAround[i, 1]));

            if (this[SearchNeighbor.X, SearchNeighbor.Y].Owner != myID)
            {
                ushort dist = CalcDistance(SearchNeighbor.X, SearchNeighbor.Y, temp.X, temp.Y);
                Potential += this[SearchNeighbor.X, SearchNeighbor.Y].Strength / (this[SearchNeighbor.X, SearchNeighbor.Y].Production + 1);
                neighborCount++;
            }
            
        }

        Potential /= (neighborCount + 1);

        return Potential;

    }





    #region Implementation

    private readonly Site[,] _sites;

    private Map(ushort width, ushort height) {
        _sites = new Site[width, height];
        for (ushort x = 0; x < width; x++) {
            for (ushort y = 0; y < height; y++) {
                _sites[x, y] = new Site();
            }
        }
    }

    private static Tuple<ushort, ushort> ParseMapSize(string mapSizeStr) {
        ushort width, height;
        var parts = mapSizeStr.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !ushort.TryParse(parts[0], out width) || !ushort.TryParse(parts[1], out height))
            throw new ApplicationException("Could not get map size from stdin during init");
        return Tuple.Create(width, height);
    }

    public static Map ParseMap(string mapSizeStr, string productionMapStr, string gameMapStr) {
        var mapSize = ParseMapSize(mapSizeStr);
        var map = new Map(mapSize.Item1, mapSize.Item2);

        var productionValues = new Queue<string>(productionMapStr.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries));

        ushort x, y;
        for (y = 0; y < map.Height; y++) {
            for (x = 0; x < map.Width; x++) {
                ushort production;
                if (!ushort.TryParse(productionValues.Dequeue(), out production))
                    throw new ApplicationException("Could not get some production value from stdin");
                map._sites[x, y].Production = production;
            }
        }

        map.Update(gameMapStr);

        return map;
    }

    #endregion

}