using System;
using System.Collections.Generic;
using System.Linq;

public class MyBot
{
    public const string MyBotName = "AisleCC";

    public static void Main(string[] args)
    {
        Console.SetIn(Console.In);
        Console.SetOut(Console.Out);

        ushort myID;
        var map = Networking.getInit(out myID);

        // ***********************************************************************
        //    Do more prep work, see rules for time limit

        Log.Setup( "error.log");


        ushort turnCount = 0;
        byte UnitsMoved = 0;

        float AggressivenessFactor = (float)0.01;

        ushort MapHeight = map.Height;
        ushort MapWidth = map.Width;
        ushort MapArea = (ushort)(MapHeight * MapWidth);

        ushort MyTotalUnits = 0;
        ushort TurnsToMove = 5;
        float PercentOfMapToGrab = (float)0.10;
        ushort MaxTopTargetsToGrab = (ushort)(PercentOfMapToGrab * MapArea); // max targets to grab
        ushort MaxUnitsToMove = 350;  //MaxUnitsToMove units that will use move

        if (MapArea > 1500)
        {
            TurnsToMove = 5;
        }
        else if (MapArea > 1000)
        {
            TurnsToMove = 6;
        }
        else
        {
            TurnsToMove = 6;
        }

        List<SiteInfo> MyUnitsEligibleToMove = new List<SiteInfo>();
        List<SiteInfo> MyTopUnitsEligibleToMove = new List<SiteInfo>();

        List<SiteInfo> DesireableTargets = new List<SiteInfo>();
        List<SiteInfo> TopDesireableTargets = new List<SiteInfo>();
        List<SiteInfo> ModifiedDesireableTargets = new List<SiteInfo>();


        // ***********************************************************************

        Networking.SendInit(MyBotName); // Acknowledge the init and begin the game


        



        var random = new Random();
        while (true)
        {
            Networking.getFrame(ref map); // Update the map to reflect the moves before this turn

            turnCount++;
            UnitsMoved = 0;
            MyTotalUnits = 0;

            //Log.Information(String.Format("Best Direction: {0}", map.FindAttackableEnemies(2, 2, myID) ));

            //Reset Variables
            DesireableTargets.Clear();
            ModifiedDesireableTargets.Clear();
            MyUnitsEligibleToMove.Clear();
            MyTopUnitsEligibleToMove.Clear();
            TopDesireableTargets.Clear();

            // first we want to build a desireable map and list so that we can gather the most desireable targets for aStar.
            var moves = new List<Move>();

            AggressivenessFactor = map.GetAggroLevel(myID);

            // collect targets and units
            for (ushort x = 0; x < map.Width; x++)
            {
                for (ushort y = 0; y < map.Height; y++)
                {
                    float desirableness = 0;

                    if (map[x, y].Owner != myID && map[x, y].Owner != 0)
                    {
                        // determine desirable targets list desirableness
                        desirableness = (float)((100 / (map[x, y].Production + 1)) / AggressivenessFactor);
                        DesireableTargets.Add(new SiteInfo(desirableness, x, y));
                    }
                    else if (map[x, y].Owner == 0)
                    {
                        // determine desirable targets list desirableness
                        desirableness = (float)(map[x, y].Strength / (map[x, y].Production + 1));
                        DesireableTargets.Add(new SiteInfo(desirableness, x, y));
                    }
                    else // if it is me
                    {
                        // if it is my own unit it is not eligible to be a target
                        MyTotalUnits++;

                        if (map[x, y].Strength >= map[x, y].Production * TurnsToMove)
                        {
                            // If is my unit and it has the strength to move add it to the MyUnitsEligibleToMove list
                            MyUnitsEligibleToMove.Add(new SiteInfo(map[x, y].Strength, x, y));
                        }
                    }
                }
            }


            
            // sort my eligible units to move by strength (In MyUnitsEligibleToMove Desirableness represents the strength of the unit)
            MyUnitsEligibleToMove = MyUnitsEligibleToMove.OrderByDescending(a => a.Desirableness).ToList();
            // only grab top so many targets
            DesireableTargets = DesireableTargets.OrderBy(a => a.Desirableness).ToList();
            
            ushort NumOfTargetsToGrab = 0;
            if (DesireableTargets.Count <= MaxTopTargetsToGrab)
            {
                NumOfTargetsToGrab = (ushort)(DesireableTargets.Count);
            }
            else
            {
                NumOfTargetsToGrab = MaxTopTargetsToGrab;
            }

            TopDesireableTargets = DesireableTargets.GetRange(0, NumOfTargetsToGrab);



            ushort NumOfTargetsToMove = 0;
            if (MyUnitsEligibleToMove.Count <= MaxUnitsToMove)
            {
                NumOfTargetsToMove = (ushort)(MyUnitsEligibleToMove.Count);
            }
            else
            {
                NumOfTargetsToMove = MaxUnitsToMove;
            }

            MyTopUnitsEligibleToMove = MyUnitsEligibleToMove.GetRange(0, NumOfTargetsToMove);




            foreach (SiteInfo Unit in MyTopUnitsEligibleToMove)
            {
                ushort DistBetweenUnitAndTarget = 0;
                SiteInfo TopTarget = new SiteInfo(0, 0, 0);

                ModifiedDesireableTargets.Clear();
                
                // calculate desirable map based on the distance from each unit to each target
                foreach (SiteInfo Target in TopDesireableTargets)
                {
                    DistBetweenUnitAndTarget = map.CalcDistance(Unit.X, Unit.Y, Target.X, Target.Y);
                    
                    float tempDesiredness = DistBetweenUnitAndTarget + Target.Desirableness;
                    
                    ModifiedDesireableTargets.Add(new SiteInfo(tempDesiredness, Target.X, Target.Y));
                }

                ModifiedDesireableTargets = ModifiedDesireableTargets.OrderBy(a => a.Desirableness).ToList();

                Direction GoDirection = DoMove(new Location { X = ModifiedDesireableTargets.First().X, Y = ModifiedDesireableTargets.First().Y }, Unit.X, Unit.Y, ref map, myID);
                
                UnitsMoved++;
                moves.Add(new Move
                {
                    Location = new Location { X = Unit.X, Y = Unit.Y },
                    Direction = GoDirection
                });
            }

            //Log.Information("sending moves");
            Networking.SendMoves(moves); // Send moves
            moves.Clear();
        }
    }






    
    static public Direction DoMove(Location Target, ushort currX, ushort currY, ref Map map, ushort myID)
    {
        List<Direction> PerimeterDirection = map.CalcPerimeterDir((short)currX, (short)currY, myID);
        Direction AttackMostEnemiesProdModDirection =  map.FindAttackableEnemiesProdMod((short)currX, (short)currY, myID);
        Direction AttackMostEnemiesDirection = map.FindAttackableEnemies((short)currX, (short)currY, myID);

        List<Direction> SearchDirections = new List<Direction>();
        Direction GoDirection = Direction.Still;

        short[,] SearchAround = new short[4, 2] { { 0, -1 }, { 1, 0 }, { 0, 1 }, { -1, 0 } };
        //                                          NORTH      EAST     SOUTH      WEST  

        short angle = map.CalcAngle(currX, currY, Target.X, Target.Y);
        float BestScore = 15000;

        if (AttackMostEnemiesDirection != Direction.Still) // there is an attackable enemy
        {
            GoDirection = AttackMostEnemiesProdModDirection; // attack in the direction that will affect the most enemies and has the highest production
        }
        else  //if (PerimeterDirection.Any()) // if on the perimeter
        {
            GoDirection = Direction.Still;

            float SecondBestScore = 15000;
            float ThirdBestScore = 15000;

            Direction SecondBestDir = Direction.Still;
            Direction ThirdBestDir = Direction.Still;

            for (int i = 0; i < 4; i++)
            {
                // Wrap SearchAround location
                Location temp = map.WrapCoord((short)(currX + SearchAround[i, 0]), (short)(currY + SearchAround[i, 1]));

                //Calculate the distance to the target
                ushort DistToTarget = map.CalcDistance(temp.X, temp.Y, Target.X, Target.Y);
                float tempScore = 15000;

                if (map[temp.X, temp.Y].Owner != myID)
                {
                    tempScore = DistToTarget * map.DeterminePotential((short)temp.X, (short)temp.Y, myID);
                }
                else
                {
                    tempScore = DistToTarget * 13;
                }

                if (tempScore < BestScore)
                {
                    BestScore = tempScore;
                    GoDirection = (Direction)(i + 1);
                }
                else if (tempScore < SecondBestScore)
                {
                    SecondBestDir = (Direction)(i + 1);
                    SecondBestScore = tempScore;
                }
                else if (tempScore < ThirdBestScore)
                {
                    ThirdBestDir = (Direction)(i + 1);
                    ThirdBestScore = tempScore;
                }
            }
            
            Location NextMoveLoc = DetermineIfBacktrack(currX, currY, GoDirection, Target, myID, ref map);

            if (NextMoveLoc.X == currX && NextMoveLoc.Y == currY)
            {
                // if going to the spot I want to leads right back here, then go to the site with the lowest score that isn't me
                List<Direction> UnownedPerimeterSites = map.CalcPerimeterDir((short)currX, (short)currY, myID);

                ushort LowestStrengthProdRatio = 500;

                foreach (Direction UnownedSiteDirection in UnownedPerimeterSites)
                {
                    Location UnOwnedLocation = map.GetLoc(currX, currY, UnownedSiteDirection);
                    ushort DistToTarget = map.CalcDistance(UnOwnedLocation.X, UnOwnedLocation.Y, Target.X, Target.Y);

                    ushort tempStrengthProdRatio = (ushort)(map[UnOwnedLocation.X, UnOwnedLocation.Y].Strength / (map[UnOwnedLocation.X, UnOwnedLocation.Y].Production + 1));

                    if (tempStrengthProdRatio < LowestStrengthProdRatio)
                    {
                        LowestStrengthProdRatio = tempStrengthProdRatio;
                        GoDirection = UnownedSiteDirection;
                    }
                }
            }
        }

        Location tempLoc = map.GetLoc(currX, currY, GoDirection);
                                                                                                                                         // ONLY MOVE IF ONE OF THE BELOW IS TRUE
        if ((map[tempLoc.X, tempLoc.Y].Owner != myID && (map[tempLoc.X, tempLoc.Y].Strength < map[currX, currY].Strength)) ||            // if I don't own it, don't attack it unless I can take it
            map[tempLoc.X, tempLoc.Y].Owner != myID && (map[tempLoc.X, tempLoc.Y].Strength==255 && map[currX, currY].Strength==255) ||   // if I don't own it, don't attack it unless we both have strength of 255
            map[tempLoc.X, tempLoc.Y].Owner != myID && map[tempLoc.X, tempLoc.Y].Owner != 0 ||                                           // if it is an enemy unit, attack it
            map[tempLoc.X, tempLoc.Y].Owner == myID)                                                                                     // if it is my own guy
        {
            GoDirection = GoDirection;
        }
        else
        {
            GoDirection = Direction.Still;
        }

        return GoDirection;
    }






    static public Location DetermineIfBacktrack(ushort currX, ushort currY, Direction GoDirection, Location Target, ushort myID, ref Map map)
    {
        short[,] SearchAround = new short[4, 2] { { 0, -1 }, { 1, 0 }, { 0, 1 }, { -1, 0 } };
        //                                          NORTH      EAST     SOUTH      WEST  

        Location GoLocation = map.GetLoc(currX, currY, GoDirection);
        float tempBestScore = 15000;
        Direction temporaryGoDirection = Direction.Still;

        for (int i = 0; i < 4; i++)
        {
            // Wrap SearchAround location
            Location temp = map.WrapCoord((short)(GoLocation.X + SearchAround[i, 0]), (short)(GoLocation.Y + SearchAround[i, 1]));

            //Calculate the distance to the target
            ushort DistToTarget = map.CalcDistance(temp.X, temp.Y, Target.X, Target.Y);
            float tempScore = 15000;

            if (map[temp.X, temp.Y].Owner != myID)
            {
                tempScore = DistToTarget * map.DeterminePotential((short)temp.X, (short)temp.Y, myID);
            }
            else
            {
                tempScore = DistToTarget * 13;
            }

            // if after it moves there it has a better position to go rather than back to the original position
            if (tempScore < tempBestScore)
            {
                tempBestScore = tempScore;
                temporaryGoDirection = (Direction)(i + 1);
            }
        }

        Location tempLocation = map.GetLoc(GoLocation.X, GoLocation.Y, temporaryGoDirection);
        
        return tempLocation;
    }
}





public class SiteInfo
{
    public ushort X { get; set; }
    public ushort Y { get; set; }
    
    // how desirable a site it
    public float Desirableness { get; set; }

    public SiteInfo(float desirableness, ushort x, ushort y)
    {
        X = x;
        Y = y;
        Desirableness = desirableness;
    }
}



public class DistancesToTarget
{
    public ushort Distance { get; set; }
    public ushort TargetX { get; set; }
    public ushort TargetY { get; set; }

    public DistancesToTarget(ushort distance, ushort targetx, ushort targety)
    {
        Distance = distance;
        TargetX = targetx;
        TargetY = targety;
    }
}
















/*
public class AStarSearching
{

    static public List<Node> AStarSearch(byte[,] map, UInt16 StartCoordX, UInt16 StartCoordY, UInt16 EndXCoord, UInt16 EndYCoord, UInt16[,] NodeIDLookupTable)
    {

        UInt16 MapXLength = (UInt16)map.GetLength(1);
        UInt16 MapYLength = (UInt16)map.GetLength(0);

        List<Node> NoPathFound = new List<Node>();

        if ((StartCoordX < 0) || (StartCoordY < 0) || (EndXCoord >= MapXLength) || (EndYCoord >= MapYLength))
        {
            // Inputs are invalid
            return NoPathFound;
        }

        byte CostToMoveNotDiagonally = 10;
        byte CostToMoveDiagonal = 14;
        UInt16 MoveWeight = 0;

        bool AllowDiagonals = false;
        bool AllowMapWrapping = true;

        byte Unwalkable = 255;

        SByte[,] SearchModifier;
        if (AllowDiagonals)
        {
            SByte[,] temp =          { { (SByte) (-1), (SByte) (-1)},          // NORTHWEST
                                       { (SByte) (-1), (SByte)  (0)},          // WEST
                                       { (SByte) (-1), (SByte)  (1)},          // SOUTHWEST
                                       { (SByte)  (0), (SByte) (-1)},          // NORTH
                                       { (SByte)  (0), (SByte)  (1)},          // SOUTH
                                       { (SByte)  (1), (SByte) (-1)},          // NORTHEAST
                                       { (SByte)  (1), (SByte)  (0)},          // EAST
                                       { (SByte)  (1), (SByte)  (1)}};         // SOUTHEAST
            SearchModifier = temp;
        }
        else    // No diagonals allowed
        {
            SByte[,] temp =          { { (SByte) (-1), (SByte)  (0)},          // WEST
                                       { (SByte)  (0), (SByte) (-1)},          // NORTH
                                       { (SByte)  (0), (SByte)  (1)},          // SOUTH
                                       { (SByte)  (1), (SByte)  (0)}};         // EAST
            SearchModifier = temp;
        }

        // SearchListStructure
        // SearchList(SearchListIndex, XCoordIndex:FScoreIndex) = [XCoord YCoord XCoordParent YCoordParent GScore HScore FScore];

        // NoGoList Structure
        // NoGoList(NoGoListIndex, XCoordIndex:FScoreIndex) = [XCoord YCoord XCoordParent YCoordParent GScore HScore FScore];


        SByte XMod = 0;
        SByte YMod = 0;

        UInt16 CurrentX = 0;
        UInt16 CurrentY = 0;

        Int16 SearchCoordX = 0;
        Int16 SearchCoordY = 0;

        UInt16 CurrentNodeID = 0;
        UInt16 tempNodeID = 0;
        UInt16 EndNodeID = NodeIDLookupTable[EndYCoord, EndXCoord];
        UInt16 StartNodeID = NodeIDLookupTable[StartCoordY, StartCoordX];

        UInt16 PreviousGScore = 0;
        UInt16 GScore = 0;
        UInt16 HScore = 0;
        UInt16 FScore = 0;

        UInt16 MoveWeightDueToMap = 0;
        UInt16 TotalMovesToFinish = 0;

        bool OnTheSearchList = false;

        List<Node> SearchList = new List<Node>();
        List<Node> NoGoList = new List<Node>();

        // Add the start coordinates to the search list
        SearchList.Add(new Node(StartCoordX, StartCoordY, 0, 0, 0, 0, 0, StartNodeID, 0));

        // Search while the SearchList is not empty
        while (SearchList.Count > 0)
        {
            SearchList = SearchList.OrderBy(x => x.GetFScore()).ToList();

            CurrentX = SearchList[0].GetXCoord();
            CurrentY = SearchList[0].GetYCoord();

            CurrentNodeID = NodeIDLookupTable[CurrentY, CurrentX];

            // Add The lowest FScore node to the NoGoList
            NoGoList.Add(SearchList[0]);

            // Remove the lowest FSCore node from the search list
            SearchList.RemoveAt(0);

            for (byte i = 0; i < SearchModifier.GetLength(0); i++)
            {
                XMod = SearchModifier[i, 0];
                YMod = SearchModifier[i, 1];

                SearchCoordX = (Int16)(CurrentX + XMod);
                SearchCoordY = (Int16)(CurrentY + YMod);

                if (AllowMapWrapping)
                {
                    // if map wrapping is allowed out of ranges of the map must be wrapped to the approriate index.

                    if (SearchCoordX < 0)
                    {
                        SearchCoordX = (short)(MapXLength-1);
                    }

                    if (SearchCoordY < 0)
                    {
                        SearchCoordY = (short)(MapYLength-1);
                    }

                    if (SearchCoordX >= MapXLength)
                    {
                        SearchCoordX = 0;
                    }

                    if (SearchCoordY >= MapYLength)
                    {
                        SearchCoordY = 0;
                    }

                }
                else // Map Wrapping is not allowed
                {
                    // if map wrapping is not allowed just continue if it is out of range of the map
                    if (SearchCoordX < 0 || SearchCoordY < 0 || SearchCoordX >= MapXLength || SearchCoordY >= MapYLength)
                    {
                        // If the Search coordinates are outside the map
                        continue;
                    }
                }

                if (map[SearchCoordY, SearchCoordX] == Unwalkable)
                {
                    // If the search coordinate is unwalkable
                    continue;
                }

                tempNodeID = NodeIDLookupTable[SearchCoordY, SearchCoordX];

                if (NoGoList.Exists(x => x.GetNodeID() == EndNodeID))
                { 
                    *****************************************************
                     * If this algorithm successfully finds a way to the *
                     * end coordinates this is where it will exit and    *
                     * return the path                                   *
                     *****************************************************

                    NoGoList = AStarSearching.TracePathBack(NoGoList, EndNodeID, StartNodeID);
                    return NoGoList;
                }

                else if (NoGoList.Exists(x => x.GetNodeID() == tempNodeID))
                {
                    // If the search coord is already on the NoGoList
                    continue;
                }



                OnTheSearchList = false;
                if (SearchList.Exists(x => x.GetNodeID() == tempNodeID))
                {
                    // If the node is already in the search list
                    OnTheSearchList = true;
                }

                if (Math.Abs(XMod) + Math.Abs(YMod) > 1)
                {
                    // Moving Diagonal
                    MoveWeight = (UInt16)CostToMoveDiagonal;
                    continue;
                }
                else
                {
                    // Moving Not Diagonal
                    MoveWeight = (UInt16)CostToMoveNotDiagonally;
                }
                
                PreviousGScore = NoGoList[NoGoList.Count()-1].GetGScore();

                MoveWeightDueToMap = map[SearchCoordY, SearchCoordX];

                // Find the current GScore
                GScore = (UInt16)(PreviousGScore + MoveWeight + MoveWeightDueToMap);

                if (!OnTheSearchList)
                {
                    if (AllowMapWrapping)
                    {
                        if (AllowDiagonals)
                        {
                            UInt16[] DistArray = new UInt16[4] { 0, 0, 0, 0 };

                            DistArray[0] = (UInt16)Math.Sqrt(Math.Pow(SearchCoordX - EndXCoord, 2) + Math.Pow(SearchCoordY - EndYCoord, 2));  // no redraw
                            DistArray[1] = (UInt16)Math.Sqrt(Math.Pow(SearchCoordX - EndXCoord + MapXLength - 1, 2) + Math.Pow(SearchCoordY - EndYCoord, 2));  // west redraw
                            DistArray[2] = (UInt16)Math.Sqrt(Math.Pow(SearchCoordX - EndXCoord, 2) + Math.Pow(SearchCoordY - EndYCoord + MapYLength - 1, 2));  // north redraw
                            DistArray[4] = (UInt16)Math.Sqrt(Math.Pow(SearchCoordX - EndXCoord - MapXLength - 1, 2) + Math.Pow(SearchCoordY - EndYCoord, 2));  // east redraw
                            DistArray[5] = (UInt16)Math.Sqrt(Math.Pow(SearchCoordX - EndXCoord, 2) + Math.Pow(SearchCoordY - EndYCoord - MapYLength - 1, 2));  // south redraw

                            HScore = DistArray.Min();
                        }
                        else
                        {
                            ushort dx = (ushort)Math.Abs(SearchCoordX - EndXCoord);
                            ushort dy = (ushort)Math.Abs(SearchCoordY - EndYCoord);

                            if (dx > (MapXLength / 2)){
                                dx = (ushort)(MapXLength - dx);
                            }
                            if (dy > (MapYLength / 2)){
                                dy = (ushort)(MapYLength - dy);
                            }

                            HScore = (ushort)(dx + dy);
                        }
                    }
                    else
                    {
                        if (AllowDiagonals)
                        {
                            TotalMovesToFinish = (UInt16)Math.Sqrt(Math.Pow(SearchCoordX - EndXCoord, 2) + Math.Pow(SearchCoordY - EndYCoord, 2));
                            HScore = (UInt16)(TotalMovesToFinish);
                        }
                        else
                        {
                            ushort dx = (ushort)Math.Abs(SearchCoordX - EndXCoord);
                            ushort dy = (ushort)Math.Abs(SearchCoordY - EndYCoord);

                            if (dx > (MapXLength / 2)){
                                dx = (ushort)(MapXLength - dx);
                            }
                            if (dy > (MapYLength / 2)){
                                dy = (ushort)(MapYLength - dy);
                            }
                            HScore = (ushort)(dx + dy);
                        }
                    }

                    FScore = (UInt16)(GScore + HScore);

                    SearchList.Add(new Node((UInt16)SearchCoordX, (UInt16)SearchCoordY,
                                                    CurrentX, CurrentY,
                                                    GScore, HScore, FScore,
                                                    tempNodeID, CurrentNodeID));

                }
                else // If already on the search list
                {
                    UInt16 NodeIndex = (UInt16)SearchList.FindIndex(x => x.GetNodeID()==tempNodeID);
                    if (SearchList[NodeIndex].GetGScore() > GScore)
                    {
                        // If the old GScore is greater than the GScore of this new path (AKA the new path is better)
                        FScore = (UInt16)(PreviousGScore + SearchList[NodeIndex].GetHScore());

                        // Record the new GScore, FScore and Parent coordinates, and parent node ID
                        SearchList[NodeIndex].SetFScore(FScore);
                        SearchList[NodeIndex].SetGScore(GScore);
                        SearchList[NodeIndex].SetXCoordParent(CurrentX);
                        SearchList[NodeIndex].SetYCoordParent(CurrentY);
                        SearchList[NodeIndex].SetParentNodeID(CurrentNodeID);
                    }
                }

            }

        }


        return NoPathFound;
    }








    static private List<Node> TracePathBack(List<Node> NoGoList, UInt16 EndNodeID, UInt16 StartNodeID)
    {
        int i = NoGoList.Count();
        int FoundIndex = 0;

        List<Node> PathList = new List<Node>();
        List<Node> TruePathList = new List<Node>();

        while (EndNodeID != StartNodeID)
        {
            
            FoundIndex = NoGoList.FindIndex(x => x.GetNodeID() == EndNodeID);

            // Add it to the PathList
            PathList.Add(NoGoList[FoundIndex]);

            EndNodeID = NoGoList[FoundIndex].GetParentNodeID();
        }

        for (int j = 0; j < PathList.Count; j++)
        {
            TruePathList.Add(PathList[PathList.Count - 1 - j]);
        }

        return TruePathList;
    }
}




















public class Node
{
    private UInt16 XCoord;
    private UInt16 YCoord;

    private UInt16 XCoordParent;
    private UInt16 YCoordParent;

    private UInt16 GScore;
    private UInt16 HScore;
    private UInt16 FScore;

    private UInt16 NodeID;
    private UInt16 ParentNodeID;


    public Node(UInt16 InXCoord,        UInt16 InYCoord,
                UInt16 InXCoordParent,  UInt16 InYCoordParent,
                UInt16 InGScore,        UInt16 InHScore,
                UInt16 InFScore,        UInt16 InNodeID,
                UInt16 InParentNodeID)
    {
        XCoord = InXCoord;
        YCoord = InYCoord;

        XCoordParent = InXCoordParent;
        YCoordParent = InYCoordParent;

        GScore = InGScore;
        HScore = InHScore;
        FScore = InFScore;

        NodeID = InNodeID;
        ParentNodeID = InParentNodeID;
    }



    public void SetXCoord(UInt16 newXCoord)
    {
        XCoord = newXCoord;
    }
    public void SetYCoord(UInt16 newYCoord)
    {
        YCoord = newYCoord;
    }
    public void SetXCoordParent(UInt16 newXCoordParent)
    {
        XCoordParent = newXCoordParent;
    }
    public void SetYCoordParent(UInt16 newYCoordParent)
    {
        YCoordParent = newYCoordParent;
    }
    public void SetGScore(UInt16 newGScore)
    {
        GScore = newGScore;
    }
    public void SetHScore(UInt16 newHScore)
    {
        HScore = newHScore;
    }
    public void SetFScore(UInt16 newFScore)
    {
        FScore = newFScore;
    }
    public void SetNodeID(UInt16 newNodeID)
    {
        NodeID = newNodeID;
    }
    public void SetParentNodeID(UInt16 newParentID)
    {
        ParentNodeID = newParentID;
    }



    public UInt16 GetXCoord()
    {
        return XCoord;
    }
    public UInt16 GetYCoord()
    {
        return YCoord;
    }
    public UInt16 GetXCoordParent()
    {
        return XCoordParent;
    }
    public UInt16 GetYCoordParent()
    {
        return YCoordParent;
    }
    public UInt16 GetGScore()
    {
        return GScore;
    }
    public UInt16 GetHScore()
    {
        return HScore;
    }
    public UInt16 GetFScore()
    {
        return FScore;
    }
    public UInt16 GetNodeID()
    {
        return NodeID;
    }
    public UInt16 GetParentNodeID()
    {
        return ParentNodeID;
    }
}
*/