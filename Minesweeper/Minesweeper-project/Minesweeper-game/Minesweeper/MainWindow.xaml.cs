// Open Beta (v0.8)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Net.Sockets;
using System.Windows.Media.Imaging;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Animation;
using System.Threading;
using System.Xml;
using System.Reflection;
using System.Drawing;
using System.Windows.Resources;

namespace Minesweeper
{
    // / <summary>
    // / Interaction logic for MainWindow.xaml
    // / </summary>
    public partial class MainWindow : Window
    {
        // A randomizer object
        private static readonly Random random = new Random();

        // An encoder object
        ASCIIEncoding asciiEnc = new ASCIIEncoding();

        // initiation of the timer
        private int TotalSeconds = 0;

        // Three values that store the game's propeties
        private int GameWidth = -1; // Max 100 don't actually put these values in, please
        private int GameHeight = -1; // Max 100
        // In the stacks gamemode this value is not the amount of INDIVIDUAL mines but rather the number of mine STACKS.
        private int GameMines = -1; // Max 100^2 = 10000

        // Stores the start position values of the game plane
        private float PlaneStartPositionHeight;
        private float PlaneStartPositionWidth;

        private bool ClickDisable = false;

        // Stores the end position values of the game plane
        public float PlaneEndPositionHeight;
        public float PlaneEndPositionWidth;

        // Stores the dimensions of the game tile
        private float GameTileHeight;
        private float GameTileWidth;

        // A list of the stacks gamemode weights
        // If this default is changed make sure to change the reset in ChangeWindowPage
        private List<int> StacksGameWeightList = new List<int> { 10, 25, 20, 15, 10, 9, 5, 3, 2, 1 };

        // Lock object
        private object _lockObject = new object();
        private object _lockPlaneDestruction = new object();

        // Timers
        private System.Windows.Threading.DispatcherTimer MinesweeperGameClockTimer = new System.Windows.Threading.DispatcherTimer();
        private System.Windows.Threading.DispatcherTimer OnlineGamePreviewUpdateTimer = new System.Windows.Threading.DispatcherTimer();
        private System.Windows.Threading.DispatcherTimer OnlineGameTimer = new System.Windows.Threading.DispatcherTimer();
        
        // Default color of a button
        private static readonly BrushConverter BrushConverter = new BrushConverter();
        private readonly System.Windows.Media.Brush DefaultButtonColor = (System.Windows.Media.Brush)BrushConverter.ConvertFrom("#FFDDDDDD");
        private readonly System.Windows.Media.Brush UndiscoveredButtonColor = (System.Windows.Media.Brush)BrushConverter.ConvertFrom("#FF8F8F8F");

        // Stores some logic data for a true right click based Click event on NewTileButton
        private bool RightMouseButtonDownFlag = false;
        // If this flag is true then a right click cannot happen when RightMouseUp is triggered
        private bool RightClickLockFlag = false;

        // The final map of the game in dictionary form.
        // Key is the ID of the tile while it's value is the amount of GameMines near it.
        // 0 means no GameMines at all and -1 means the tile is a mine itself
        // 8 is the maximum amount for value, because of course a tile is surrounded by 8 tiles at max
        // That is unless the minigame supports more then one mine per tile
        // one mine = -1, two mines = -2, three mines = -3. And so on...
        private Dictionary<int, int> GameTileMap = new Dictionary<int, int>();

        // A dictionary that stores the visability value of every tile
        // Basically if it's discovered by the player or not
        // GameMines are always hidden (until death)
        private Dictionary<int, bool> TileDiscoveryState = new Dictionary<int, bool>();

        // A dictionary containing all of the flagged tiles' indexes
        // along with the amount of flags placed on them (To support multi-mine mingames)
        private Dictionary<int, int> FlaggedTiles = new Dictionary<int, int>();

        // Takes a game creation page and returns it's string name
        private Dictionary<int, string> MinigameAliaeses = new Dictionary<int, string> { { 149, "Vanilla" }, { 151, "Stacks" }, { 200, "CO-OP" }, { 202, "Battle Royale" } };

        // false means no game is running, true means a game is running
        private bool gameState = false;

        private byte windowPage = 0;

        // Stores the game page of the online game that is being or starting to be played
        private byte OnlineGame = 0;

        private int TileFontSize = 35;

        // A flag to prevent initiation duplication
        private bool OnlineGameInitiated = false;

        // A token to be used in online matchmaking to be recognized as the host of the game.
        // null is a default string because sending an empty one is like sending nothing which is big oof.
        private string hostToken = "null";
        // A bool that stores if this instance is a proven host
        private bool isHost = false;

        // A bool that keeps track if the application is connected to the host console
        private bool isConnected = false;

        // The socket used to stabely talk to the host console
        private Socket stableSocket;

        private int VotedTile = 0;

        private int PlayersInGame = 0;

        private List<int> OldCoopTotalVotes = new List<int>();
        private List<int> NewCoopTotalVotes = new List<int>();

        // The lower and upper bounds the tile border can be. Said border changes in thickness upon being selected as a vote by one of the participating players,
        // currently only in the CO-OP multiplayer minigame.
        // The first element (lower bound) of this tuple is the default border size for the tiles, regardless of minigame.
        // This value is not to be changed from anywhere else in the program.
        private Tuple<double, double> TileBorderBounds = Tuple.Create(0.6, 10.0);

        // Used to tell the physical location of a tile (Uses an int not a Button)
        // Both start at 0 (Basically with width/height 4 you'll have column/line 0, 1, 2, 3 -> next revolution
        private int ColumnOfTile(int TileToCheck, int GameWidth) => (int)((TileToCheck - 1) % GameWidth);
        private int LineOfTile(int TileToCheck, int GameWidth) => (TileToCheck + TileToCheck / GameWidth - 1) / (GameWidth + 1);

        public MainWindow()
        {
            InitializeComponent();

            // sets up the minesweeper timer
            MinesweeperGameClockTimer.Tick += new EventHandler(MinesweeperGameClockTimer_Tick);
            MinesweeperGameClockTimer.Interval = TimeSpan.FromSeconds(1);

            // Sets up the Online Game Preview update timer (used later on)
            OnlineGamePreviewUpdateTimer.Tick += new EventHandler(OnlineGamePreviewUpdateTimer_Tick);
            // Ticks 10 times a second
            OnlineGamePreviewUpdateTimer.Interval = TimeSpan.FromMilliseconds(100);

            OnlineGameTimer.Tick += new EventHandler(OnlineGameTimer_Tick);
            OnlineGameTimer.Interval = TimeSpan.FromMilliseconds(100);

            
        }

        private void OnlineGameTimer_Tick(object sender, EventArgs e)
        {
            lock (_lockObject)
            {
                if (isConnected)
                {
                    stableSocket.Send(asciiEnc.GetBytes("CALL:ONLINE_GAME_TICK"), SocketFlags.None);
                    string[] TickResponse = HandleServerInput(stableSocket).Split('\n');

                    // .Length checking is a hackfix to prevent the update preview data from bleeding here (Fix may or may not come in the future)
                    // Debug
                    if (OnlineGame == 201)
                    {
                        // For OnlineGame == 201, the tick reponse is DATA:TICK\n{CoopVotesTileNumber}
                        if (TickResponse[0] != "NULL" && TickResponse.Length == 2)
                        {
                            OldCoopTotalVotes = NewCoopTotalVotes;
                            if (TickResponse[1].Split(',')[0] != "")
                                NewCoopTotalVotes = TickResponse[1].Split(',').ToList().ConvertAll(int.Parse);
                            else
                                NewCoopTotalVotes = new List<int>();

                            CoopTagTiles();
                        }
                    }
                    else
                        throw new NotImplementedException();
                }
            }
        }

        // Generates the physical tile grid (as buttons) on the main window
        private void GenerateNewGamePlane()
        {
            // Saves the postions of key points in the game plane
            PlaneStartPositionHeight = (float)GamePlaneBorder.BorderThickness.Top + (float)GamePlaneBorder.Margin.Top;
            PlaneStartPositionWidth = (float)GamePlaneBorder.BorderThickness.Left + (float)GamePlaneBorder.Margin.Left;

            // Saves the dimensions of the game tiles
            GameTileHeight = (float)((GamePlaneBorder.Height - GamePlaneBorder.BorderThickness.Top * 2) / GameHeight);
            GameTileWidth = (float)((GamePlaneBorder.Width - GamePlaneBorder.BorderThickness.Left * 2) / GameWidth);

            // Saves the postion of the end point (By a tile perception)
            PlaneEndPositionHeight = (float)GamePlaneBorder.Margin.Top
                                   + (float)(GamePlaneBorder.Height - GamePlaneBorder.BorderThickness.Bottom)
                                   - GameTileHeight;
            PlaneEndPositionWidth = (float)GamePlaneBorder.Margin.Left
                                    + (float)(GamePlaneBorder.Width - GamePlaneBorder.BorderThickness.Right)
                                    - GameTileWidth;

            // The actual button (Tile) generation
            for (int Tile = 1; Tile <= GameHeight * GameWidth; Tile++)
            {
                // Common propeties of every button
                Button NewTileButton = new Button
                {
                    Height = GameTileHeight,
                    Width = GameTileWidth,

                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Left,

                    // Puts every tile in the correct position
                    Margin = new Thickness(PlaneStartPositionWidth + (GameTileWidth * ColumnOfTile(Tile, GameWidth)), PlaneStartPositionHeight + (GameTileHeight * LineOfTile(Tile, GameWidth)), 0, 0),

                    Name = "TileButton" + Tile.ToString(),

                    Style = MinesweeperGameWindow.Resources["buttonGlowOverride"] as Style,

                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,

                    FontWeight = FontWeights.Heavy,

                    FontSize = (TileFontSize * 10) / (GameHeight < GameWidth ? GameHeight : GameWidth),

                    BorderThickness = new Thickness(TileBorderBounds.Item1, TileBorderBounds.Item1, TileBorderBounds.Item1, TileBorderBounds.Item1),
                    BorderBrush = System.Windows.Media.Brushes.Black
                };

                NewTileButton.MouseRightButtonDown += new MouseButtonEventHandler(NewTileButton_RightMouseButtonDown);
                NewTileButton.MouseRightButtonUp += new MouseButtonEventHandler(NewTileButton_RightMouseButtonUp);


                NewTileButton.Click += new RoutedEventHandler(NewTileButton_Click);
                NewTileButton.MouseLeave += new MouseEventHandler(NewTileButton_MouseLeave);


                NewTileButton.Content = "";

                // Adds the new tile to the TileDiscoveryState
                ChangeTileDiscoveryState(NewTileButton, false);
                
                MinesweeperMainGrid.Children.Add(NewTileButton);

                Debug.WriteLine("Generating tiles: " + Tile.ToString() + " of " + (GameHeight * GameWidth).ToString());
            }
            Debug.WriteLine("Finished generating tiles!");

            ClickDisable = false;
        }

        // Cleans up and hides the 
        // Maps and generates the values for each tile, with respect to user input
        private void GenerateMapAndAllTiles(int StartingTileNumber)
        {
            FlaggedTiles = new Dictionary<int, int>();

            // Forbidden Mine Tiles are tiles that CANNOT be mines. This usually applies to the starting tile and all other 8 tiles neighboring it.
            List<int> ForbiddenMineTileNumbers = new List<int>();
            GameTileMap = new Dictionary<int, int>();

            //Handles the ForbiddenMineTileNumbers list
            ForbiddenMineTileNumbers.Add(StartingTileNumber);
            foreach (int ForbiddenMineTileNumber in GetAllNeighboringTileNumbers(GetTileFromNumber(StartingTileNumber), GameWidth, GameHeight))
                ForbiddenMineTileNumbers.Add(ForbiddenMineTileNumber);

            // A list that countains all tile IDs
            // List<dynamic> TemporaryTileMap = Enumerable.Range(1, GameWidth * GameHeight).Select(i => (dynamic)i).ToList(); ;
            #region CreateMineMap
            // A list that countains all random indexes that got used already
            List<int> UsedTiles = new List<int>();
            
            for (int mine = 0; mine < GameMines; mine++)
            {
                // -1 is an arbitrary value that cannot exist in the game plane as a tile ID
                int randomTileId = -1;

                while (UsedTiles.Contains(randomTileId) || randomTileId == -1 || ForbiddenMineTileNumbers.Contains(randomTileId))
                {
                    randomTileId = random.Next(1, GameWidth * GameHeight + 1);
                }

                // Coop game stack mine generation
                if (windowPage == 152)
                {
                    int randomWeightRoll = random.Next(0, 100);

                    for (int i = 0; i < StacksGameWeightList.Count; i++)
                    {
                        if (Enumerable.Range(SumIntList(StacksGameWeightList.ToList(), 0, i), StacksGameWeightList[i]).Contains(randomWeightRoll))
                        {
                            GameTileMap.Add(randomTileId, -1 * (i+1));
                            break;
                        }
                    }
                }
                // Vanilla mode
                else
                {
                    GameTileMap.Add(randomTileId, -1);
                }
                UsedTiles.Add(randomTileId);
            }
            #endregion

            // Maps the default values of every undefined tile
            for (int Tile = 1; Tile <= GameHeight * GameWidth; Tile++)
            {
                //Maps the default value for a tile
                if (!GameTileMap.ContainsKey(Tile))
                {
                    // Default value for a tile
                    GameTileMap.Add(Tile, 0);
                }

                FlaggedTiles[Tile] = 0;
            }

            // Maps the number value of every tile
            for (int Tile = 1; Tile <= GameHeight * GameWidth; Tile++)
            {
                if (GameTileMap[Tile] >= 0)
                {
                    foreach (int NeighborTileNumber in GetAllNeighboringTileNumbers(GetTileFromNumber(Tile), GameWidth, GameHeight))
                    {
                        if (NeighborTileNumber != -1)
                        {
                            if (GameTileMap[NeighborTileNumber] < 0)
                                GameTileMap[Tile] += -GameTileMap[NeighborTileNumber];
                        }
                    }
                }
            }

            // Adds extra flags for the stacks gamemode
            MinesweeperFlagCounter.Text = CountIndividualMines().ToString();

            // Restarts the clock, -1 is actually instantly being event triggered into 0 so it's actually starting from 0
            // TotalSeconds = -1;
            TotalSeconds = 0;
        }

        // These events make up the TileButtons clicking types (Left click, right click and the RightAndLeftClick)
        #region NewTileButton click events
        private void NewTileButton_MouseLeave(object sender, EventArgs e)
        {
            if (RightMouseButtonDownFlag)
                RightClickLockFlag = true;
        }
        private void NewTileButton_RightMouseButtonDown(object sender, EventArgs e) => RightMouseButtonDownFlag = true;
        // Actual tile right click event
        private void NewTileButton_RightMouseButtonUp(object sender, EventArgs e)
        {
            Button CurrentTileButton = sender as Button;

            if (ClickDisable)
                return;

            if (windowPage == 150)
            {
                // Check to see if the game is still ongoing
                if (gameState)
                {
                    if (RightMouseButtonDownFlag && !RightClickLockFlag)
                    {
                        // Check if the tile that the player is trying to flag is not discovered
                        // and that there are flags left to actually perform a flag
                        if (TileDiscoveryState[GetTileNumber(CurrentTileButton)] == false)
                        {
                            // Add the flag
                            if (FlaggedTiles[GetTileNumber(CurrentTileButton)] == 0 && int.Parse(MinesweeperFlagCounter.Text) > 0)
                            {
                                MinesweeperFlagCounter.Text = (int.Parse(MinesweeperFlagCounter.Text) - 1).ToString();
                                FlaggedTiles[GetTileNumber(CurrentTileButton)] = 1;
                                ColorCodeTile(CurrentTileButton);

                                GameWon();
                            }
                            // Remove the flag
                            else if (FlaggedTiles[GetTileNumber(CurrentTileButton)] != 0)
                            {
                                FlaggedTiles[GetTileNumber(CurrentTileButton)] = 0;
                                MinesweeperFlagCounter.Text = (int.Parse(MinesweeperFlagCounter.Text) + 1).ToString();
                                ColorCodeTile(CurrentTileButton);
                            }
                        }
                    }
                    else if (RightMouseButtonDownFlag && RightClickLockFlag)
                        RightClickLockFlag = false;
                }
            }
            else if (windowPage == 152)
            {
                // Check to see if the game is still ongoing
                if (gameState)
                {
                    if (RightMouseButtonDownFlag && !RightClickLockFlag)
                    {
                        // Check if the tile that the player is trying to flag is not discovered
                        // and that there are flags left to actually perform a flag
                        if (TileDiscoveryState[GetTileNumber(CurrentTileButton)] == false)
                        {
                            // Remove all flags 
                            if ((Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
                            {
                                MinesweeperFlagCounter.Text = (int.Parse(MinesweeperFlagCounter.Text) + FlaggedTiles[GetTileNumber(CurrentTileButton)]).ToString();
                                FlaggedTiles[GetTileNumber(CurrentTileButton)] = 0;
                            }
                            // Increment one flag
                            else if (!(Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)))
                            {
                                if (FlaggedTiles[GetTileNumber(CurrentTileButton)] < 10)
                                {
                                    if (int.Parse(MinesweeperFlagCounter.Text) > 0)
                                    {
                                        MinesweeperFlagCounter.Text = (int.Parse(MinesweeperFlagCounter.Text) - 1).ToString();
                                        FlaggedTiles[GetTileNumber(CurrentTileButton)] += 1;
                                    }
                                }
                                else
                                {
                                    MinesweeperFlagCounter.Text = (int.Parse(MinesweeperFlagCounter.Text) + 10).ToString();
                                    FlaggedTiles[GetTileNumber(CurrentTileButton)] = 0;
                                }
                            }
                            // Decrement one flag
                            else
                            {
                                if (FlaggedTiles[GetTileNumber(CurrentTileButton)] > 0)
                                {
                                    MinesweeperFlagCounter.Text = (int.Parse(MinesweeperFlagCounter.Text) + 1).ToString();
                                    FlaggedTiles[GetTileNumber(CurrentTileButton)] -= 1;
                                }
                                else
                                {
                                    if (int.Parse(MinesweeperFlagCounter.Text) >= 10)
                                    {
                                        MinesweeperFlagCounter.Text = (int.Parse(MinesweeperFlagCounter.Text) - 10).ToString();
                                        FlaggedTiles[GetTileNumber(CurrentTileButton)] = 10;
                                    }
                                    else if (int.Parse(MinesweeperFlagCounter.Text) >= 1)
                                    {
                                        FlaggedTiles[GetTileNumber(CurrentTileButton)] = int.Parse(MinesweeperFlagCounter.Text);
                                        MinesweeperFlagCounter.Text = "0";
                                    }
                                }
                            }

                            ColorCodeTile(CurrentTileButton);
                            GameWon();
                        }
                    }
                    else if (RightMouseButtonDownFlag && RightClickLockFlag)
                        RightClickLockFlag = false;
                }
            }
            else if (windowPage == 201)
            {
                if (RightMouseButtonDownFlag && !RightClickLockFlag)
                    CoopGameSubmitAction("RightClick", GetTileNumber(CurrentTileButton));
                else if (RightMouseButtonDownFlag && RightClickLockFlag)
                    RightClickLockFlag = false;
            }
            else
                throw new NotImplementedException();

            RightMouseButtonDownFlag = false;
        }
        // Click event for each tile button, used for revealing tiles
        private void NewTileButton_Click(object sender, EventArgs e)
        {
            Button CurrentTileButton = sender as Button;

            if (ClickDisable)
                return;

            if (windowPage == 150)
            {
                // checks to see if the game has been generated already
                if (!gameState && !ClickDisable)
                {
                    //Map all tiles and start the game
                    GenerateMapAndAllTiles(GetTileNumber(CurrentTileButton));
                    gameState = true;
                    MinesweeperGameClockTimer.Start();
                }
                if (TileDiscoveryState[GetTileNumber(CurrentTileButton)] == false && FlaggedTiles[GetTileNumber(CurrentTileButton)] == 0 && !ClickDisable)
                    RevealTile(CurrentTileButton);
                // This block is for the right+left click feature that reveals all neighbor tiles
                else if (TileDiscoveryState[GetTileNumber(CurrentTileButton)] && !ClickDisable && RightMouseButtonDownFlag)
                    RevealTile(CurrentTileButton, true);
            }
            else if (windowPage == 152)
            {
                // checks to see if the game has been generated already
                if (!gameState && !ClickDisable)
                {
                    //Map all tiles and start the game
                    GenerateMapAndAllTiles(GetTileNumber(CurrentTileButton));
                    gameState = true;
                    MinesweeperGameClockTimer.Start();
                }
                if (TileDiscoveryState[GetTileNumber(CurrentTileButton)] == false && FlaggedTiles[GetTileNumber(CurrentTileButton)] == 0 && !ClickDisable)
                    RevealTile(CurrentTileButton);
                // This block is for the right+left click feature that reveals all neighbor tiles
                else if (TileDiscoveryState[GetTileNumber(CurrentTileButton)] && !ClickDisable && RightMouseButtonDownFlag)
                    RevealTile(CurrentTileButton, true);
            }
            else if (windowPage == 201)
            {
                if (!RightMouseButtonDownFlag)
                {
                    CoopGameSubmitAction("LeftClick", GetTileNumber(CurrentTileButton));
                }
                else
                {
                    CoopGameSubmitAction("RightAndLeftClick", GetTileNumber(CurrentTileButton));
                    RightClickLockFlag = true;
                }
            }
            else
                throw new NotImplementedException();
        }
        #endregion
         
        private void VirtualLeftClick(int TileNumber)
        {
            // checks to see if the game has been generated already
            if (!gameState && isHost)
            {
                //Map all tiles and start the game
                GenerateMapAndAllTiles(TileNumber);
            }

            gameState = true;
            MinesweeperGameClockTimer.Start();
            RevealTile(GetTileFromNumber(TileNumber));
        }

        private void VirtualRightClick(int TileNumber)
        {
            Button TileButton = GetTileFromNumber(TileNumber);
            // Add the flag
            if (FlaggedTiles[TileNumber] == 0 && int.Parse(MinesweeperFlagCounter.Text) > 0)
            {
                MinesweeperFlagCounter.Text = (int.Parse(MinesweeperFlagCounter.Text) - 1).ToString();
                FlaggedTiles[TileNumber] = 1;
                ColorCodeTile(TileButton);
                GameWon();
            }
            // Remove the flag
            else if (FlaggedTiles[TileNumber] != 0)
            {
                if (TileDiscoveryState[TileNumber] == true)
                {
                    TileButton.Content = GameTileMap[TileNumber].ToString();
                }
                else
                {
                    TileButton.Content = "";
                }

                MinesweeperFlagCounter.Text = (int.Parse(MinesweeperFlagCounter.Text) + 1).ToString();
                FlaggedTiles[TileNumber] = 0;
                ColorCodeTile(TileButton);
            }
        }

        // Right and left click is an event where a left click accoures while the right mouse button is held down
        private void VirtualRightAndLeftClick(int TileNumber)
        {
            RevealTile(GetTileFromNumber(TileNumber), true);
        }

        // A utility function, tells if string is made out of digits only (a natural number kinda) and is not empty
        private bool ContainsDigitsOnly(string str) => str.All(c => char.IsDigit(c)) && str != "";

        // Handles the minesweeper timer Tick event. basically all of the timer code goes there
        private void MinesweeperGameClockTimer_Tick(object sender, EventArgs e)
        {
            // Adds a second to the timer count
            TotalSeconds += 1;

            // Prints a nice format of the timer count to the game clock
            MinesweeperGameClock.Text = (TotalSeconds / 600).ToString() + (TotalSeconds / 60).ToString() + ":"
                                      + ((TotalSeconds % 60) / 10).ToString() + ((TotalSeconds % 60) % 10).ToString();
        }

        // Starts the game basically, hides the pregame gui and reveals the ingame gui along with starting ingame mechanics (timer) and generating a game plane
        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateGamePropeties(windowPage))
            {
                GameMines = int.Parse(GeneralGameCreationMinesInput.Text);
                GameHeight = int.Parse(GeneralGameCreationHeightInput.Text);
                GameWidth = int.Parse(GeneralGameCreationWidthInput.Text);

                ChangeWindowPage((byte)((int)windowPage + 1), out windowPage);

                GenerateNewGamePlane();
                // Generates the physical game plane
            }
        }

        // The restart button
        // Handles a game restart if requested by the player
        private void GameRestartButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult RestartMessage = MessageBoxResult.No;

            if (isConnected && !isHost)
            {
                MessageBox.Show("Only the host can restart the game.", "Game restart denied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            // Views a confirmation message box if the game is still ongoing, skips it otherwise
            else if (gameState)
            {
                RestartMessage = MessageBox.Show("Are you sure you want to restart the game?", "Game restart", MessageBoxButton.YesNo);
            }
            // Sends a message box for confirmation
            if (RestartMessage == MessageBoxResult.Yes || (gameState == false && (isConnected == isHost)))
            {
                if (OnlineGame == 0)
                    RestartGame();
                else if (OnlineGame == 201)
                {
                    stableSocket.Send(asciiEnc.GetBytes("CALL:RESTART_GAME"), SocketFlags.None);
                    HandleServerInput(stableSocket);
                    return;
                }
                else
                    throw new NotImplementedException();
            }
        }

        private void RestartGame()
        {
            // Removes existing game related stored values
            TileDiscoveryState = new Dictionary<int, bool>();
            GameTileMap = new Dictionary<int, int>();

            // Undiscovers all tiles
            for (int Tile = 1; Tile <= GameWidth * GameHeight; Tile++)
            {
                ChangeTileDiscoveryState(GetTileFromNumber(Tile), false);
            }

            // Restart the restart button
            GameRestartButton.Content = ":)";

            //Closes the game if exists
            gameState = false;

            MinesweeperGameClockTimer.Stop();

            //Fixes the clock visually
            TotalSeconds = 0;
            MinesweeperGameClock.Text = (TotalSeconds / 600).ToString() + (TotalSeconds / 60).ToString() + ":"
                                         + ((TotalSeconds % 60) / 10).ToString() + ((TotalSeconds % 60) % 10).ToString();

            //Restarts the flag counter
            MinesweeperFlagCounter.Text = GameMines.ToString();

            //Returns the click disable value back to it's original value
            ClickDisable = false;
        }

        private void GeneralGameCreationEasyPresetButton_Click(object sender, RoutedEventArgs e)
        {
            // Changes the input boxes's text to the Easy gamemode preset
            GeneralGameCreationHeightInput.Text = "10";
            GeneralGameCreationMinesInput.Text = "10";
            GeneralGameCreationWidthInput.Text = "10";
        }

        private int SumIntList(List<int> list, int min, int max)
        {
            int output = 0;
            for (int i = min; i < max; i++)
            {
                output += list[i];
            }
            return output;
        }

        private void GeneralGameCreationNormalPresetButton_Click(object sender, RoutedEventArgs e)
        {
            // Changes the input boxes's text to the Normal gamemode preset
            GeneralGameCreationHeightInput.Text = "20";
            GeneralGameCreationWidthInput.Text = "20";
            GeneralGameCreationMinesInput.Text = "50";
        }

        private void GeneralGameCreationHardPresetButton_Click(object sender, RoutedEventArgs e)
        {
            // Changes the input boxes's text to the Hard gamemode preset
            GeneralGameCreationHeightInput.Text = "35";
            GeneralGameCreationWidthInput.Text = "35";
            GeneralGameCreationMinesInput.Text = "120";
        }

        private bool ValidateGamePropeties(byte creationPageOfGame)
        {
            // Range of offline game pages: [149, 199], for the creation pages it's every odd number in that range
            bool IsPageOfflineGameCreationPage = creationPageOfGame >= 149 && creationPageOfGame <= 199 && creationPageOfGame % 2 != 0;

            // Range of online game pags: [200, 250], for the creation pages it's every even number in that range
            bool IsPageOnlineGameCreationPage = creationPageOfGame >= 200 && creationPageOfGame <= 250 && creationPageOfGame % 2 == 0;

            if (IsPageOnlineGameCreationPage)
            {
                if (ValidateEndPointInput(OnlineGameCreationMenuIpInput.Text, OnlineGameCreationMenuPortInput.Text))
                {
                    // Individual online game propeties check logic starts here!

                    // CO-OP
                    if (creationPageOfGame == 200)
                    {
                        // Doesn't have any propeties that CAN be invalid atm.
                        return ValidateCorePropeties();
                    }
                    // Battle Royale
                    else if (creationPageOfGame == 202)
                    {

                    }
                    else
                        throw new ArgumentException("Propeties check for the given game does not exist yet! (It is a game creation page though) Given page: " + creationPageOfGame.ToString());
                }
            }
            else if (IsPageOfflineGameCreationPage)
            {
                // Individual offline game propeties check logic starts here!

                // Vanilla
                if (creationPageOfGame == 149)
                {
                    return ValidateCorePropeties();
                }
                // Stacks
                else if (creationPageOfGame == 151)
                {
                    return SumIntList(StacksGameWeightList, 0, StacksGameWeightList.Count) == 100 && ValidateCorePropeties();
                }
                else
                    throw new ArgumentException("Propeties check for the given game does not exist yet! (It is a game creation page though) Given page: " + creationPageOfGame.ToString());
            }
            else
                throw new ArgumentException("Page given is neither an offline game creation page nor an online game creation page! Page given: " + creationPageOfGame.ToString());

            // This one is just to trick the complier to validating the thing since it doesn't return anything in the exceptions (aka not all code paths return a value)
            return false;
        }

        /// <summary>
        /// Helper function of ValidateGamePropeties
        /// 
        /// This functions only validates the core proepties, also known as the height, width and mines boxes as they fundementally
        /// have the same requirements as in the Vanilla minigame. This is used in most minigame propeties validations.
        /// 
        /// It may not be used in all of them, they may be some exceptions.
        /// </summary>
        /// <returns>Returns a valid or invalid in bool form</returns>
        private bool ValidateCorePropeties(bool theoretical = false)
        {
            if (!theoretical)
            {
                if (ContainsDigitsOnly(GeneralGameCreationHeightInput.Text) && ContainsDigitsOnly(GeneralGameCreationMinesInput.Text) && ContainsDigitsOnly(GeneralGameCreationWidthInput.Text))
                {
                    // Height input validity
                    if (int.Parse(GeneralGameCreationHeightInput.Text) <= 50 && int.Parse(GeneralGameCreationHeightInput.Text) > 3)
                    {
                        // Width input validity
                        if (int.Parse(GeneralGameCreationWidthInput.Text) <= 50 && int.Parse(GeneralGameCreationWidthInput.Text) > 3)
                        {
                            // Mines input validity
                            if (int.Parse(GeneralGameCreationMinesInput.Text) <= int.Parse(GeneralGameCreationHeightInput.Text) * int.Parse(GeneralGameCreationWidthInput.Text) - 9 && int.Parse(GeneralGameCreationMinesInput.Text) > 0)
                                return true;
                        }
                    }
                }

                return false;
            }
            else
            {
                if (GameHeight <= 50 && GameHeight > 3)
                {
                    if (GameWidth <= 50 && GameHeight > 3)
                    {
                        if (GameMines <= GameHeight * GameWidth - 9 && GameMines > 0)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// A function that changes pages (GUI layouts) in the Window
        /// </summary>
        /// <param name="pageToSwapTo">A byte representing a page to swap to, this will update the globalPage variable to whatever is inputed to pageToSwapTo</param>
        /// <param name="globalPage">An application global variable that stores the current page for reference and navigation</param>
        private void ChangeWindowPage(byte pageToSwapTo, out byte globalPage)
        {

            // dev mode thing ~~ actually I like it so I'm keeping it like that
            MinesweeperGameWindow.Title = "Minesweeper - At page: " + pageToSwapTo.ToString();

            #region Visibility block
            Visibility OfflineHubGuiVisibility = Visibility.Hidden;

            Visibility OnlineGamePreviewGuiVisibility = Visibility.Hidden;

            Visibility VanillaGameCreationGuiVisibility = Visibility.Hidden;
            Visibility VanillaGameGuiVisibility = Visibility.Hidden;

            Visibility StacksGameCreationGuiVisibility = Visibility.Hidden;
            Visibility StacksGameGuiVisibility = Visibility.Hidden;

            Visibility MatchConnectionMenuGuiVisibility = Visibility.Hidden;
            Visibility OnlineHubGuiVisibility = Visibility.Hidden;

            Visibility CoopGameCreationGuiVisibility = Visibility.Hidden;
            Visibility CoopGameGuiVisibility = Visibility.Hidden;

            Visibility BattleRoyaleGameCreationGuiVisibility = Visibility.Hidden;
            Visibility BattleRoyaleGameGuiVisibility = Visibility.Hidden;
            #endregion

            // Offline and Online games get 50 pages each

            // Pages [200, 250] are purely reserved for online minigames.
            // Pages [149, 199] are purely reserved for offline minigames.
            // Don't ask why I'm reserving that many, lol.

            // A bool that checks that if pageToSwapTo is trying to swap to using the ranges in the comment above.
            // Pages go like this: offline game creation page, game page, game creation page, game page.
            bool IsPageOfflineGameCreationPage = pageToSwapTo >= 149 && pageToSwapTo <= 199 && pageToSwapTo % 2 != 0;
            bool IsPageOfflineGamePage = pageToSwapTo >= 149 && pageToSwapTo <= 199 && pageToSwapTo % 2 == 0;

            // Pages go like this: online game creation page, online game 
            bool IsPageOnlineGameCreationPage = pageToSwapTo >= 200 && pageToSwapTo <= 250 && pageToSwapTo % 2 == 0;
            bool IsPageOnlineGamePage = pageToSwapTo >= 200 && pageToSwapTo <= 250 && pageToSwapTo % 2 != 0;

            // Plans: Add an offline minigame hub, add an online minigame hub.

            // This if block controls Gui that is exclusive to either ALL of the game pages, or ALL of the none game pages.
            // This makes handeling GUI such as the minesweeper label much easier.

            // Handles game pages of any kind.
            if (IsPageOfflineGamePage || IsPageOnlineGamePage)
            {
                MinesweeperLabel.Visibility = Visibility.Hidden;
                MinesweeperLabel.IsEnabled = false;

                GamePlaneBorder.Visibility = Visibility.Visible;
                GamePlaneBorder.IsEnabled = true;

                // Controls the game clock 
                MinesweeperGameClock.Text = "00:00";
                MinesweeperGameClock.IsEnabled = true;
                MinesweeperGameClock.Visibility = Visibility.Visible;

                // Controls the flag counter
                MinesweeperFlagCounter.IsEnabled = true;
                MinesweeperFlagCounter.Visibility = Visibility.Visible;
                MinesweeperFlagCounter.Text = GameMines.ToString();

                // Controls the restart button
                GameRestartButton.Content = ":)";
                GameRestartButton.IsEnabled = true;
                GameRestartButton.Visibility = Visibility.Visible;

                // Controls the back button
                GameGuiBackButton.IsEnabled = true;
                GameGuiBackButton.Visibility = Visibility.Visible;
            }
            else
            {
                MinesweeperLabel.Visibility = Visibility.Visible;
                MinesweeperLabel.IsEnabled = true;

                GamePlaneBorder.Visibility = Visibility.Hidden;
                GamePlaneBorder.IsEnabled = false;

                // Controls the game clock 
                MinesweeperGameClock.Text = "00:00";
                MinesweeperGameClock.IsEnabled = false;
                MinesweeperGameClock.Visibility = Visibility.Hidden;

                // Controls the flag counter
                MinesweeperFlagCounter.IsEnabled = false;
                MinesweeperFlagCounter.Visibility = Visibility.Hidden;

                // Controls the restart button
                GameRestartButton.Content = ":)";
                GameRestartButton.IsEnabled = false;
                GameRestartButton.Visibility = Visibility.Hidden;

                // Controls the back button
                GameGuiBackButton.IsEnabled = false;
                GameGuiBackButton.Visibility = Visibility.Hidden;

                GameHeight = 0;
                GameWidth = 0;
                GameMines = 0;

                gameState = false;
                ClickDisable = false;

                GameTileMap = new Dictionary<int, int>();
                TileDiscoveryState = new Dictionary<int, bool>();
                FlaggedTiles = new Dictionary<int, int>();

                MinesweeperGameClockTimer.Stop();
            }

            // Handles game creation pages of any kind.
            if (IsPageOnlineGameCreationPage || IsPageOfflineGameCreationPage)
            {
                GeneralGameCreationWidthInput.Text = "";
                GeneralGameCreationWidthInput.Visibility = Visibility.Visible;
                GeneralGameCreationWidthInput.IsEnabled = true;
                
                GeneralGameCreationHeightInput.Text = "";
                GeneralGameCreationHeightInput.Visibility = Visibility.Visible;
                GeneralGameCreationHeightInput.IsEnabled = true;

                GeneralGameCreationMinesInput.Text = "";
                GeneralGameCreationMinesInput.Visibility = Visibility.Visible;
                GeneralGameCreationMinesInput.IsEnabled = true;

                GeneralGameCreationHeightTextBlock.Visibility = Visibility.Visible;
                GeneralGameCreationHeightTextBlock.IsEnabled = true;

                GeneralGameCreationWidthTextBlock.Visibility = Visibility.Visible;
                GeneralGameCreationWidthTextBlock.IsEnabled = true;

                GeneralGameCreationMinesTextBlock.Visibility = Visibility.Visible;
                GeneralGameCreationMinesTextBlock.IsEnabled = true;

                // Shows and enables the preset buttons
                GeneralGameCreationEasyPresetButton.Visibility = Visibility.Visible;
                GeneralGameCreationEasyPresetButton.IsEnabled = true;

                GeneralGameCreationNormalPresetButton.Visibility = Visibility.Visible;
                GeneralGameCreationNormalPresetButton.IsEnabled = true;

                GeneralGameCreationHardPresetButton.Visibility = Visibility.Visible;
                GeneralGameCreationHardPresetButton.IsEnabled = true;

                GeneralGameCreationExpertPresetButton.Visibility = Visibility.Visible;
                GeneralGameCreationExpertPresetButton.IsEnabled = true;
            }
            else
            {
                GeneralGameCreationWidthInput.Text = "";
                GeneralGameCreationWidthInput.Visibility = Visibility.Hidden;
                GeneralGameCreationWidthInput.IsEnabled = false;

                GeneralGameCreationMinesTextBlock.Visibility = Visibility.Hidden;
                GeneralGameCreationMinesTextBlock.IsEnabled = false;

                GeneralGameCreationHeightInput.Text = "";
                GeneralGameCreationHeightInput.Visibility = Visibility.Hidden;
                GeneralGameCreationHeightInput.IsEnabled = false;

                GeneralGameCreationHeightTextBlock.Visibility = Visibility.Hidden;
                GeneralGameCreationHeightTextBlock.IsEnabled = false;

                GeneralGameCreationMinesInput.Text = "";
                GeneralGameCreationMinesInput.Visibility = Visibility.Hidden;
                GeneralGameCreationMinesInput.IsEnabled = false;

                GeneralGameCreationWidthTextBlock.Visibility = Visibility.Hidden;
                GeneralGameCreationWidthTextBlock.IsEnabled = false;

                // Shows and enables the preset buttons
                GeneralGameCreationEasyPresetButton.Visibility = Visibility.Hidden;
                GeneralGameCreationEasyPresetButton.IsEnabled = false;

                GeneralGameCreationNormalPresetButton.Visibility = Visibility.Hidden;
                GeneralGameCreationNormalPresetButton.IsEnabled = false;

                GeneralGameCreationHardPresetButton.Visibility = Visibility.Hidden;
                GeneralGameCreationHardPresetButton.IsEnabled = false;

                GeneralGameCreationExpertPresetButton.Visibility = Visibility.Hidden;
                GeneralGameCreationExpertPresetButton.IsEnabled = false;
            }

            // Handles offline game creation pages.
            if (IsPageOfflineGameCreationPage)
            {
                OfflineGameCreationBackButton.Visibility = Visibility.Visible;
                OfflineGameCreationBackButton.IsEnabled = true;

                OfflineGameCreationStartButton.Visibility = Visibility.Visible;
                OfflineGameCreationStartButton.IsEnabled = true;
            }
            else
            {
                OfflineGameCreationBackButton.Visibility = Visibility.Hidden;
                OfflineGameCreationBackButton.IsEnabled = false;

                OfflineGameCreationStartButton.Visibility = Visibility.Hidden;
                OfflineGameCreationStartButton.IsEnabled = false;
            }

            // Handles online game creation pages
            if (IsPageOnlineGameCreationPage)
            {
                OnlineGameCreationMenuBackButton.Visibility = Visibility.Visible;
                OnlineGameCreationMenuBackButton.IsEnabled = true;

                OnlineGameCreationMenuIpInput.Text = "127.0.0.1";
                OnlineGameCreationMenuIpInput.Visibility = Visibility.Visible;
                OnlineGameCreationMenuIpInput.IsEnabled = true;

                OnlineGameCreationMenuPortInput.Text = "42069";
                OnlineGameCreationMenuPortInput.Visibility = Visibility.Visible;
                OnlineGameCreationMenuPortInput.IsEnabled = true;

                OnlineGameCreationMenuStartButton.Visibility = Visibility.Visible;
                OnlineGameCreationMenuStartButton.IsEnabled = true;

                OnlineGameCreationMenuColenForAdressLabel.Visibility = Visibility.Visible;
                OnlineGameCreationMenuColenForAdressLabel.IsEnabled = true;

                OnlineGameCreationMenuExistingConsoleCheckBox.Visibility = Visibility.Visible;
                OnlineGameCreationMenuExistingConsoleCheckBox.IsEnabled = true;
            }
            else
            {
                OnlineGameCreationMenuBackButton.Visibility = Visibility.Hidden;
                OnlineGameCreationMenuBackButton.IsEnabled = false;

                OnlineGameCreationMenuIpInput.Text = "127.0.0.1";
                OnlineGameCreationMenuIpInput.Visibility = Visibility.Hidden;
                OnlineGameCreationMenuIpInput.IsEnabled = false;

                OnlineGameCreationMenuPortInput.Text = "42069";
                OnlineGameCreationMenuPortInput.Visibility = Visibility.Hidden;
                OnlineGameCreationMenuPortInput.IsEnabled = false;

                OnlineGameCreationMenuStartButton.Visibility = Visibility.Hidden;
                OnlineGameCreationMenuStartButton.IsEnabled = false;

                OnlineGameCreationMenuColenForAdressLabel.Visibility = Visibility.Hidden;
                OnlineGameCreationMenuColenForAdressLabel.IsEnabled = false;

                OnlineGameCreationMenuExistingConsoleCheckBox.Visibility = Visibility.Hidden;
                OnlineGameCreationMenuExistingConsoleCheckBox.IsEnabled = false;
            }

            #region Individual exceptions
            if (pageToSwapTo != 3)
            {
                OnlineGamePreviewUpdateTimer.Stop();
            }

            if (!IsPageOnlineGamePage)
            {
                OnlineGameTimer.Stop();
            }

            if (pageToSwapTo != 151)
            {
                GeneralGameCreationMinesTextBlock.Text = "Mines:";
            }

            else if (pageToSwapTo != 201)
            {
                OnlineGameTimer.Stop();
            }
            #endregion

            // Page 0 is the offline game hub.
            if (pageToSwapTo == 0)
            {
                OfflineHubGuiVisibility = Visibility.Visible;
                MinesweeperLabel.Content = "MINESWEEPER";
            }
            // Page 1 is the match connection menu
            else if (pageToSwapTo == 1)
            {
                MatchConnectionMenuGuiVisibility = Visibility.Visible;

                MinesweeperLabel.Content = "MINESWEEPER - online!";
            }
            // Page 2 is the online hub
            else if (pageToSwapTo == 2)
            {
                OnlineHubGuiVisibility = Visibility.Visible;

                MinesweeperLabel.Content = "MINESWEEPER - online!";
            }
            // Page 3 is the online game preview page
            else if (pageToSwapTo == 3)
            {
                OnlineGamePreviewGuiVisibility = Visibility.Visible;

                MinesweeperLabel.Content = "Game Preview";
                OnlineGamePreviewUpdateTimer.Start();
            }

            #region Offline games GUI
            /// First game-start
            else if (pageToSwapTo == 149)
            {
                VanillaGameCreationGuiVisibility = Visibility.Visible;
                MinesweeperLabel.Content = "MINESWEEPER";

            }
            // Page 1 is offline game GUI
            else if (pageToSwapTo == 150)
            {
                VanillaGameGuiVisibility = Visibility.Visible;
            }
            /// First game-end
            /// Second game-start
            else if (pageToSwapTo == 151)
            {
                StacksGameCreationGuiVisibility = Visibility.Visible;

                StacksGameWeightList = new List<int> { 10, 25, 20, 15, 10, 9, 5, 3, 2, 1 };
                StacksGameCreationWeightSelectionComboBox.SelectedIndex = 0;

                MinesweeperLabel.Content = "MINESWEEPER - STACKS";
                GeneralGameCreationMinesTextBlock.Text = "Stacks:";
            }
            else if (pageToSwapTo == 152)
            {
                StacksGameGuiVisibility = Visibility.Visible;
            }
            #endregion
            
            #region Online games GUI
            /// First game-start (CO-OP)
            else if (pageToSwapTo == 200)
            {
                CoopGameCreationGuiVisibility = Visibility.Visible;

                MinesweeperLabel.Content = "MINESWEEPER - CO-OP!";
            }
            else if (pageToSwapTo == 201)
            {
                CoopGameGuiVisibility = Visibility.Visible;

                OnlineGameTimer.Start();
            }
            /// First game-end

            /// Second game-start (Battle Royale)
            else if (pageToSwapTo == 202)
            {
                BattleRoyaleGameCreationGuiVisibility = Visibility.Visible;

                MinesweeperLabel.FontSize -= 10;
                MinesweeperLabel.Content = "MINESWEEPER - Battle Royale!";
            }
            else if (pageToSwapTo == 203)
            {
                BattleRoyaleGameGuiVisibility = Visibility.Visible;
            }
            /// Second game-end
            #endregion
            
            else
            {
                throw new ArgumentException("Page attempting to traverse to in ChangeWindowPage does not exist! pageToSwapTo: " + pageToSwapTo.ToString());
            }
            
            globalPage = pageToSwapTo;

            #region Controls the Offline Hub GUI
            OfflineHubToMatchConnectionMenuNavigationButton.Visibility = OfflineHubGuiVisibility;
            OfflineHubToMatchConnectionMenuNavigationButton.IsEnabled = pageToSwapTo == 0;

            OfflineHubButton1.Visibility = OfflineHubGuiVisibility;
            OfflineHubButton1.IsEnabled = pageToSwapTo == 0;

            OfflineHubButton2.Visibility = OfflineHubGuiVisibility;
            OfflineHubButton2.IsEnabled = pageToSwapTo == 0;
            #endregion

            #region Controls the vanilla game creation GUI
            //Clean up game and go to main menu



            #endregion

            #region Controls the vanilla game GUI

            #endregion

            #region Controls the stacks game creation GUI
            StacksGameCreationPercentageDisplayTextBox.Visibility = StacksGameCreationGuiVisibility;
            StacksGameCreationPercentageDisplayTextBox.IsEnabled = pageToSwapTo == 151;

            StacksGameCreationWeightInputTextBox.Visibility = StacksGameCreationGuiVisibility;
            StacksGameCreationWeightInputTextBox.IsEnabled = pageToSwapTo == 151;

            StacksGameCreationWeightSelectionComboBox.Visibility = StacksGameCreationGuiVisibility;
            StacksGameCreationWeightSelectionComboBox.IsEnabled = pageToSwapTo == 151;

            StacksGameCreationSetAllButton.Visibility = StacksGameCreationGuiVisibility;
            StacksGameCreationSetAllButton.IsEnabled = pageToSwapTo == 151;

            StacksGameCreationSetDefaultButton.Visibility = StacksGameCreationGuiVisibility;
            StacksGameCreationSetDefaultButton.IsEnabled = pageToSwapTo == 151;

            StacksGameCreationRawInputTextBox.Visibility = StacksGameCreationGuiVisibility;
            StacksGameCreationRawInputTextBox.IsEnabled = pageToSwapTo == 151;

            StacksGameCreationSetRawInputButton.Visibility = StacksGameCreationGuiVisibility;
            StacksGameCreationSetRawInputButton.IsEnabled = pageToSwapTo == 151;
            #endregion

            #region Controls the stacks game GUI
            #endregion

            #region Controls Match Connection Menu GUI
            MatchConnectionMenuBackButton.Visibility = MatchConnectionMenuGuiVisibility;
            MatchConnectionMenuBackButton.IsEnabled = pageToSwapTo == 1;

            MatchConnectionMenuConnectButton.Visibility = MatchConnectionMenuGuiVisibility;
            MatchConnectionMenuConnectButton.IsEnabled = pageToSwapTo == 1;

            MatchConnectionMenuToOnlineHubNavigationButton.Visibility = MatchConnectionMenuGuiVisibility;
            MatchConnectionMenuToOnlineHubNavigationButton.IsEnabled = pageToSwapTo == 1;

            MatchConnectionMenuIpInput.Visibility = MatchConnectionMenuGuiVisibility;
            MatchConnectionMenuIpInput.IsEnabled = pageToSwapTo == 1;
            MatchConnectionMenuIpInput.Text = "127.0.0.1";

            MatchConnectionMenuPortInput.Visibility = MatchConnectionMenuGuiVisibility;
            MatchConnectionMenuPortInput.IsEnabled = pageToSwapTo == 1;
            MatchConnectionMenuPortInput.Text = "42069";

            MatchConnectionMenuColenForAdressLabel.Visibility = MatchConnectionMenuGuiVisibility;
            MatchConnectionMenuColenForAdressLabel.IsEnabled = pageToSwapTo == 1;
            #endregion

            #region Controls the Online Hub GUI
            OnlineHubButton1.Visibility = OnlineHubGuiVisibility;
            OnlineHubButton1.IsEnabled = pageToSwapTo == 2;

            OnlineHubButton2.Visibility = OnlineHubGuiVisibility;
            OnlineHubButton2.IsEnabled = pageToSwapTo == 2;

            OnlineHubBackButton.Visibility = OnlineHubGuiVisibility;
            OnlineHubBackButton.IsEnabled = pageToSwapTo == 2;
            #endregion

            #region Controls Coop Game Creation Menu GUI
            CoopCreationMoveModeCheckButton.Visibility = CoopGameCreationGuiVisibility;
            CoopCreationMoveModeCheckButton.IsEnabled = pageToSwapTo == 200;

            CoopCreationRandomModeCheckLabel.Visibility = CoopGameCreationGuiVisibility;
            CoopCreationRandomModeCheckLabel.IsEnabled = pageToSwapTo == 200;

            CoopCreationVoteModeCheckLabel.Visibility = CoopGameCreationGuiVisibility;
            CoopCreationVoteModeCheckLabel.IsEnabled = pageToSwapTo == 200;
            #endregion

            #region Controls Coop Game Gui
            #endregion

            #region Controls the Online Game Preview GUI
            OnlineGamePreviewHostDisplayerLabel.Visibility = OnlineGamePreviewGuiVisibility;
            OnlineGamePreviewHostDisplayerLabel.IsEnabled = pageToSwapTo == 3;

            OnlineGamePreviewPlayerCountLabel.Visibility = OnlineGamePreviewGuiVisibility;
            OnlineGamePreviewPlayerCountLabel.IsEnabled = pageToSwapTo == 3;

            OnlineGamePreviewMinigameDisplayerLabel.Visibility = OnlineGamePreviewGuiVisibility;
            OnlineGamePreviewMinigameDisplayerLabel.IsEnabled = pageToSwapTo == 3;

            OnlineGamePreviewQuitMatchButton.Visibility = OnlineGamePreviewGuiVisibility;
            OnlineGamePreviewQuitMatchButton.IsEnabled = pageToSwapTo == 3;

            OnlineGamePreviewStartGameButton.Visibility = OnlineGamePreviewGuiVisibility;
            OnlineGamePreviewStartGameButton.IsEnabled = pageToSwapTo == 3;
            #endregion
        }

        // Removes all of the tile buttons.
        private void DestroyGamePlane()
        {
            lock (_lockPlaneDestruction)
            {
                List<UIElement> MainGridChildren = new List<UIElement>();
                foreach (UIElement Child in MinesweeperMainGrid.Children)
                {
                    MainGridChildren.Add(Child);
                }

                foreach (UIElement Child in MainGridChildren)
                {
                    Button ButtonChild;
                    if (Child is Button)
                    {
                        ButtonChild = Child as Button;
                        if (ButtonChild.Name.Contains("TileButton"))
                        {
                            MinesweeperMainGrid.Children.Remove(ButtonChild);
                        }
                    }
                }
            }
        }

        private void GeneralGameCreationExpertPresetButton_Click(object sender, RoutedEventArgs e)
        {
            // Changes the input boxes's text to the Expert gamemode preset
            GeneralGameCreationHeightInput.Text = "50";
            GeneralGameCreationWidthInput.Text = "50";
            GeneralGameCreationMinesInput.Text = "300";
        }

        private int GetTileNumber(Button TileToCheck)
        {
            string Output = "";

            foreach (char c in TileToCheck.Name)
            {
                if (char.IsDigit(c))
                {
                    Output += c.ToString();
                }
            }

            return int.Parse(Output);
        }

        private Button GetTileFromNumber(int TileNumber)
        {
            foreach (var control in MinesweeperMainGrid.Children)
            {
                if (control is Button CurrentTile)
                {
                    if (CurrentTile.Name.Contains("TileButton" + TileNumber))
                    {
                        return CurrentTile;
                    }
                }
            }

            return null;
        }

        // This function is responsible for all of the tile related graphics, it does more then just change the colors.
        private void ColorCodeTile(Button TileToColor)
        {
            // Color codes the text for a button thing
            if (TileDiscoveryState[GetTileNumber(TileToColor)] == false)
            {
                TileToColor.Background = UndiscoveredButtonColor;
                TileToColor.Content = "";
            }
            else if (GameTileMap[GetTileNumber(TileToColor)] < 0)
                TileToColor.Foreground = System.Windows.Media.Brushes.Black;
            else if (GameTileMap[GetTileNumber(TileToColor)] == 0)
                TileToColor.Foreground = System.Windows.Media.Brushes.White;
            else if (GameTileMap[GetTileNumber(TileToColor)] == 1)
                TileToColor.Foreground = System.Windows.Media.Brushes.Blue;
            else if (GameTileMap[GetTileNumber(TileToColor)] == 2)
                TileToColor.Foreground = System.Windows.Media.Brushes.Green;
            else if (GameTileMap[GetTileNumber(TileToColor)] == 3)
                TileToColor.Foreground = System.Windows.Media.Brushes.Red;
            else if (GameTileMap[GetTileNumber(TileToColor)] == 4)
                TileToColor.Foreground = System.Windows.Media.Brushes.DarkBlue;
            else if (GameTileMap[GetTileNumber(TileToColor)] == 5)
                TileToColor.Foreground = System.Windows.Media.Brushes.Maroon;
            else if (GameTileMap[GetTileNumber(TileToColor)] == 6)
                TileToColor.Foreground = System.Windows.Media.Brushes.LightSeaGreen;
            else if (GameTileMap[GetTileNumber(TileToColor)] == 7)
                TileToColor.Foreground = System.Windows.Media.Brushes.Black;
            else if (GameTileMap[GetTileNumber(TileToColor)] == 8)
                TileToColor.Foreground = System.Windows.Media.Brushes.Gray;
            else if (GameTileMap[GetTileNumber(TileToColor)] == 80)
                TileToColor.Foreground = System.Windows.Media.Brushes.Gold;
            else if (GameTileMap[GetTileNumber(TileToColor)] > 8)
                TileToColor.Foreground = System.Windows.Media.Brushes.Purple;

            if (TileDiscoveryState[GetTileNumber(TileToColor)])
            {
                TileToColor.Background = DefaultButtonColor;
                if (GameTileMap[GetTileNumber(TileToColor)] > 0)
                    TileToColor.Content = GameTileMap[GetTileNumber(TileToColor)].ToString();
                else if (GameTileMap[GetTileNumber(TileToColor)] == 0)
                    TileToColor.Content = "";
                else if (GameTileMap[GetTileNumber(TileToColor)] < 0 && windowPage == 152)
                    TileToColor.Content = (GameTileMap[GetTileNumber(TileToColor)] * -1).ToString() + "X";
            }
            else if (FlaggedTiles[GetTileNumber(TileToColor)] > 0)
            {
                if (windowPage == 152)
                    TileToColor.Content = FlaggedTiles[GetTileNumber(TileToColor)].ToString() + "F";
                else
                    TileToColor.Content = "";
                TileToColor.Background = System.Windows.Media.Brushes.Yellow;
                TileToColor.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void CoopTagTiles()
        {
            lock (_lockPlaneDestruction)
            {

                double middleStepStone = 2.0;
                foreach (int oldVote in OldCoopTotalVotes)
                {
                    GetTileFromNumber(oldVote).BorderThickness = new Thickness(TileBorderBounds.Item1, TileBorderBounds.Item1, TileBorderBounds.Item1, TileBorderBounds.Item1);
                    GetTileFromNumber(oldVote).BorderBrush = System.Windows.Media.Brushes.Black;
                }
                foreach (int newVote in NewCoopTotalVotes)
                {
                    if (NewCoopTotalVotes.Count(votedTile => votedTile == newVote) == 1)
                    {
                        GetTileFromNumber(newVote).BorderThickness = new Thickness(middleStepStone, middleStepStone, middleStepStone, middleStepStone);
                    }
                    else if (NewCoopTotalVotes.Count(votedTile => votedTile == newVote) > 0)
                    {
                        double currentBorderThickness = middleStepStone + ((double)NewCoopTotalVotes.Count / PlayersInGame * (TileBorderBounds.Item2 - middleStepStone));
                        GetTileFromNumber(newVote).BorderThickness = new Thickness(currentBorderThickness, currentBorderThickness, currentBorderThickness, currentBorderThickness);
                    }

                    if (VotedTile == newVote)
                    {
                        GetTileFromNumber(newVote).BorderBrush = System.Windows.Media.Brushes.Cyan;
                    }
                    else
                    {
                        GetTileFromNumber(newVote).BorderBrush = System.Windows.Media.Brushes.Black;
                    }
                }
            }
        }

        // Returns the mine count directly from the GameTileMap
        // This should really only be different from the value GameMines only in the Stacks Gamemode currently.
        private int CountIndividualMines()
        {
            int output = 0;
            
            foreach (int val in GameTileMap.Values)
            {
                if (val < 0)
                    output -= val;
            }

            return output;
        }

        private void ChangeTileDiscoveryState(Button TileToEdit, bool DiscoveryState)
        {
            // Discover tile
            if (DiscoveryState)
            {
                TileToEdit.Background = DefaultButtonColor;
                TileDiscoveryState[GetTileNumber(TileToEdit)] = true;
                ColorCodeTile(TileToEdit);

                if (FlaggedTiles[GetTileNumber(TileToEdit)] != 0 && GameTileMap[GetTileNumber(TileToEdit)] != -1)
                {
                    MinesweeperFlagCounter.Text = (int.Parse(MinesweeperFlagCounter.Text) + 1).ToString();
                }
            }
            // Undiscover tile
            else
            {
                TileToEdit.Background = UndiscoveredButtonColor;
                TileToEdit.Content = "";
                TileDiscoveryState[GetTileNumber(TileToEdit)] = false;
            }
        }

        private void GameLost(Button MineDiscovered)
        {
            for (int Tile = 1; Tile < GameHeight * GameWidth; Tile++)
            {
                if ((GameTileMap[Tile] >= 0 && FlaggedTiles[Tile] > 0) || (FlaggedTiles[Tile] > 0 && GameTileMap[Tile] < 0 && -GameTileMap[Tile] != FlaggedTiles[Tile]))
                    GetTileFromNumber(Tile).Background = System.Windows.Media.Brushes.Purple;

                if (GameTileMap[Tile] < 0 && FlaggedTiles[Tile] == 0)
                {
                    Button CurrentTile = GetTileFromNumber(Tile);

                    ChangeTileDiscoveryState(CurrentTile, true);
                    CurrentTile.Background = System.Windows.Media.Brushes.SaddleBrown;
                }
            }

            MineDiscovered.Background = System.Windows.Media.Brushes.Orange;
            GameRestartButton.Content = ":(";
            ClickDisable = true;
            gameState = false;
            MinesweeperGameClockTimer.Stop();
        }

        /// <summary>
        /// This function when called checks if all of the needed requirements are met for a game to be considered won
        /// The reqs are: All tiles must be discovered and thoes that aren't must be properly flagged (With no false positives)
        /// </summary>
        private void GameWon()
        {
            // This foreach is basically the win conditions.
            // Loops through every tile.
            foreach (int Tile in GameTileMap.Keys)
            {
                // This if block checks if each tile does not meet the requirements for a win, if it doesn't it returns the function.
                if (GameTileMap[Tile] > 0 && !TileDiscoveryState[Tile] || (GameTileMap[Tile] < 0 && FlaggedTiles[Tile] != -GameTileMap[Tile]) || GameTileMap[Tile] < 0 && TileDiscoveryState[Tile] || !gameState)
                {
                    return;
                }
            }

            //Start a party for the player except not really
            //Win code goes here!
            gameState = false;
            ClickDisable = true;
            GameRestartButton.Content = "D:";
            MinesweeperGameClockTimer.Stop();
            //GameRestartButton.RenderTransform = new RotateTransform(90, 0, 0);
        }

        private void GameGuiBackButton_Click(object sender, RoutedEventArgs e)
        {
            if ((gameState && windowPage == 150) || (gameState && windowPage == 152))
            {
                MessageBoxResult MainMenuConfirmation = MessageBox.Show("You have an ongoing game! Are you sure you want to go back?", "Go back", MessageBoxButton.YesNo);

                if (MainMenuConfirmation == MessageBoxResult.Yes)
                {
                    gameState = false;

                    DestroyGamePlane();
                    ChangeWindowPage((byte)(windowPage - 1), out windowPage);
                }
            }
            else if ((windowPage == 150 && !gameState) || (windowPage == 152 && !gameState))
            {
                gameState = false;

                DestroyGamePlane();
                ChangeWindowPage((byte)(windowPage - 1), out windowPage);
            }

            if (OnlineGame != 0 && isConnected)
            {
                if (isHost)
                {
                    if (MessageBox.Show("Are you sure you want to close the match?", "Close match", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        QuitMatch();
                }
                else
                {
                    if (MessageBox.Show("Are you sure you want to leave the match?", "Leave match", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        QuitMatch();
                }
            }
        }

        private void QuitMatch()
        {
            // Stop the update timer
            OnlineGamePreviewUpdateTimer.Stop();

            OnlineGameTimer.Stop();

            if (OnlineGameInitiated)
                DestroyGamePlane();
            // Go back to the connect page
            ChangeWindowPage(1, out windowPage);

            // Resets the OnlineGame flag
            OnlineGame = 0;

            // Resets the initiation flag
            OnlineGameInitiated = false;

            // Call leave command to the server...
            stableSocket.Send(asciiEnc.GetBytes("CALL:QUIT"), SocketFlags.None);

            stableSocket.Close();
        }

        /// <summary>
        /// Reveals a given tile, if isOrigin is set to true, it'll reveal all of the tiles neighboring the given tile WITHOUT revealing the given tile.
        /// </summary>
        /// <param name="TileToReveal"></param>
        /// <param name="isOrigin"></param>
        private void RevealTile(Button TileToReveal, bool isOrigin = false)
        {
            if (!isOrigin)
            {
                if (TileDiscoveryState[GetTileNumber(TileToReveal)])
                    return;
                if (GameTileMap[GetTileNumber(TileToReveal)] < 0 && FlaggedTiles[(GetTileNumber(TileToReveal))] == 0)
                {
                    GameLost(TileToReveal);
                    return;
                }
                else if (GameTileMap[GetTileNumber(TileToReveal)] == 0)
                {
                    // First class tiles are tiles that'll be checked/are being checked while second class tiles will be checked on check later
                    // To not disturb the first class layer check
                    List<int> FirstClassTileNumbers = new List<int>();
                    List<int> SecondClassTileNumbers = new List<int>();
                    List<int> AlreadyGroupedTileNumbers = new List<int>();

                    int IsChanging = -1;

                    FirstClassTileNumbers.Add(GetTileNumber(TileToReveal));
                    ChangeTileDiscoveryState(TileToReveal, true);

                    while (AlreadyGroupedTileNumbers.Count <= GameHeight * GameWidth - GameMines && IsChanging != AlreadyGroupedTileNumbers.Count)//Math.Pow(8, GameWidth / 2))// && false) //Shouldn't be true but instead maybe some empty list (perhaps first class or second class)
                    {
                        IsChanging = AlreadyGroupedTileNumbers.Count;

                        //Go from first class to second class
                        foreach (int Tile in FirstClassTileNumbers)
                        {
                            foreach (int NeighborTileNumber in GetAllNeighboringTileNumbers(GetTileFromNumber(Tile), GameWidth, GameHeight))
                            {
                                if (NeighborTileNumber >= 0)
                                {
                                    if (!AlreadyGroupedTileNumbers.Contains(NeighborTileNumber))
                                    {
                                        if (GameTileMap[NeighborTileNumber] == 0)
                                        {
                                            SecondClassTileNumbers.Add(NeighborTileNumber);
                                            AlreadyGroupedTileNumbers.Add(NeighborTileNumber);
                                        }
                                        else if (GameTileMap[NeighborTileNumber] >= 0)
                                        {
                                            ChangeTileDiscoveryState(GetTileFromNumber(NeighborTileNumber), true);
                                        }
                                    }
                                }
                            }
                        }
                        FirstClassTileNumbers = new List<int>();

                        //Discover and convert all second classes to first class
                        foreach (int Tile in SecondClassTileNumbers)
                        {
                            ChangeTileDiscoveryState(GetTileFromNumber(Tile), true);
                            FirstClassTileNumbers.Add(Tile);
                        }
                        SecondClassTileNumbers = new List<int>();
                    }
                }
                else if (GameTileMap[GetTileNumber(TileToReveal)] > 0 && FlaggedTiles[GetTileNumber(TileToReveal)] == 0)
                    //Makes the tile discovered
                    ChangeTileDiscoveryState(TileToReveal, true);

                // Checks if game won
                GameWon();
            }
            else
            {
                // Is any of the neighboring tiles an unchecked mine?
                bool revealFatal = false;
                // Count of flags around the origin Tile.
                byte flagCount = 0;

                foreach (int NeighborTileNumber in GetAllNeighboringTileNumbers(TileToReveal, GameWidth, GameHeight))
                {
                    if (NeighborTileNumber != -1)
                    {
                        if (FlaggedTiles[NeighborTileNumber] == 0 && GameTileMap[NeighborTileNumber] < 0)
                        {
                            revealFatal = true;
                        }
                        else if (FlaggedTiles[NeighborTileNumber] != 0)
                            flagCount++;
                    }
                }

                // Prevents exploiting of the flag system that allowes spamming flags and using the game itself to solve out the correct flags
                if (flagCount > GameTileMap[GetTileNumber(TileToReveal)])
                    return;
                // gay

                if (revealFatal)
                {
                    foreach (int NeighborTileNumber in GetAllNeighboringTileNumbers(TileToReveal, GameWidth, GameHeight))
                    {
                        if (NeighborTileNumber != -1)
                            if (FlaggedTiles[NeighborTileNumber] == 0 && GameTileMap[NeighborTileNumber] < 0)
                                RevealTile(GetTileFromNumber(NeighborTileNumber));
                    }
                }
                else
                {
                    foreach (int NeighborTileNumber in GetAllNeighboringTileNumbers(TileToReveal, GameWidth, GameHeight))
                    {
                        if (NeighborTileNumber != -1)
                            RevealTile(GetTileFromNumber(NeighborTileNumber));
                    }
                }
            }
        }
    
        private int[] GetAllNeighboringTileNumbers(Button OriginTile, int GameWidth, int GameHeight)
        {
            int[] Output = {-1, -1, -1, -1, -1, -1, -1, -1};

            // Tile to the left
            if (ColumnOfTile(GetTileNumber(OriginTile), GameWidth) > 0)
                Output[0] = GetTileNumber(OriginTile) - 1;
            // Tile to the right
            if (ColumnOfTile(GetTileNumber(OriginTile), GameWidth) < GameWidth - 1)
                Output[1] = GetTileNumber(OriginTile) + 1;
            // Tile above
            if (LineOfTile(GetTileNumber(OriginTile), GameWidth) > 0 && LineOfTile(GetTileNumber(OriginTile), GameWidth) > 0)
                Output[2] = GetTileNumber(OriginTile) - GameWidth;
            // Tile below
            if (LineOfTile(GetTileNumber(OriginTile), GameWidth) < GameHeight - 1)
                Output[3] = GetTileNumber(OriginTile) + GameWidth;
            // Tile left up
            if (ColumnOfTile(GetTileNumber(OriginTile), GameWidth) > 0 && LineOfTile(GetTileNumber(OriginTile), GameWidth) > 0)
                Output[4] = GetTileNumber(OriginTile) - 1 - GameWidth;
            // Tile right up
            if (ColumnOfTile(GetTileNumber(OriginTile), GameWidth) < GameWidth - 1 && LineOfTile(GetTileNumber(OriginTile), GameWidth) > 0)
                Output[5] = GetTileNumber(OriginTile) + 1 - GameWidth;
            // Tile left down
            if (ColumnOfTile(GetTileNumber(OriginTile), GameWidth) > 0 && LineOfTile(GetTileNumber(OriginTile), GameWidth) < GameHeight - 1)
                Output[6] = GetTileNumber(OriginTile) - 1 + GameWidth;
            // Tile right down
            if (ColumnOfTile(GetTileNumber(OriginTile), GameWidth) < GameWidth - 1 && LineOfTile(GetTileNumber(OriginTile), GameWidth) < GameHeight - 1)
                Output[7] = GetTileNumber(OriginTile) + 1 + GameWidth;

            return Output;
        }

        private void OfflineHubToMatchConnectionMenuNavigationButton_Click(object sender, RoutedEventArgs e)
        {
            ChangeWindowPage(1, out windowPage);
        }

        private void MatchConnectionMenuBackButton_Click(object sender, RoutedEventArgs e)
        {
            ChangeWindowPage(0, out windowPage);
        }

        private void MatchConnectionMenuToOnlineHubNavigationButton_Click(object sender, RoutedEventArgs e)
        {
            ChangeWindowPage(2, out windowPage);
        }

        private void OnlineGameCreationMenuBackButton_Click(object sender, RoutedEventArgs e)
        {
            MinesweeperLabel.FontSize = 60;
            ChangeWindowPage(2, out windowPage);
        }

        // Offline games [149, 199]
        private void OfflineHubButton1_Click(object sender, RoutedEventArgs e)
        {
            ChangeWindowPage(149, out windowPage);
        }

        private void OfflineHubButton2_Click(object sender, RoutedEventArgs e)
        {
            ChangeWindowPage(151, out windowPage);
        }

        // Online games [200, 250]
        private void OnlineHubButton1_Click(object sender, RoutedEventArgs e)
        {
            ChangeWindowPage(200, out windowPage);
        }

        private void OnlineHubButton2_Click(object sender, RoutedEventArgs e)
        {
            // Debug
            MessageBox.Show("Not implemented yet!", "Not lethal error", MessageBoxButton.OK, MessageBoxImage.Error);
           // ChangeWindowPage(202, out windowPage);
        }

        private void OnlineHubBackButton_Click(object sender, RoutedEventArgs e)
        {
            ChangeWindowPage(1, out windowPage);
        }

        private void OfflineGameCreationBackButton_Click(object sender, RoutedEventArgs e)
        {
            ChangeWindowPage(0, out windowPage);
        }

        private void CoopCreationMoveModeCheckButton_Click(object sender, RoutedEventArgs e)
        {
            // Uses content to make the button act as a checkbox since I have no clue how to use XMAL styles lol
            if ((string)CoopCreationMoveModeCheckButton.Content == "0")
            {
                CoopCreationMoveModeCheckButton.Content = "1";

                ImageBrush brush = new ImageBrush
                {
                    ImageSource = BitmapFrame.Create(Application.GetResourceStream(new Uri("Resources/BlackArrowUp.png", UriKind.Relative)).Stream),
                    Stretch = Stretch.Fill
                };

                CoopCreationMoveModeCheckButton.Background = brush;
            }
            else if ((string)CoopCreationMoveModeCheckButton.Content == "1")
            {
                CoopCreationMoveModeCheckButton.Content = "0";


                ImageBrush brush = new ImageBrush
                {
                    ImageSource = BitmapFrame.Create(Application.GetResourceStream(new Uri("Resources/BlackArrowDown.png", UriKind.Relative)).Stream),
                    Stretch = Stretch.Fill
                };

                CoopCreationMoveModeCheckButton.Background = brush;
                // CoopCreationMoveModeCheckButton.Foreground
            }
        }
        
        /// <summary>
        /// Starts the online processes of the project, initializes online play!
        /// </summary>
        private void OnlineGameCreationMenuStartButton_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateGamePropeties(windowPage))
            {
                isHost = false;

                // The outputed string that will be sent to the python script
                string rawGamePropeties = OnlineGameCreationMenuIpInput.Text + "\n" + OnlineGameCreationMenuPortInput.Text + "\n" + windowPage.ToString() + "\n"
                                        + GeneralGameCreationWidthInput.Text + "\n" + GeneralGameCreationHeightInput.Text + "\n" + GeneralGameCreationMinesInput.Text + "\n";

                // CO-OP minigame propeties
                if (windowPage == 200)
                {
                    rawGamePropeties += (string)CoopCreationMoveModeCheckButton.Content == "1" ? "Vote" : "Random";
                }
                // Battle Royale minigame propeteies
                if (windowPage == 202)
                {
                    throw new NotImplementedException();
                }

                #region Initial Setup
                // Server for the initial setup of the python script
                try
                {
                    // Hollow variables to satisfy compiler
                    IPAddress ipAddr = IPAddress.Parse("0.0.0.0");
                    TcpListener myListener = new TcpListener(ipAddr, 0);
                    try
                    {
                        /// While these two seem broken in a theoretical case where the script runs instantly, before the AcceptSocket() method starts
                        /// Even in that case it would catch the request from the script since socket.socket.connect will attempt connection repeatedly for about 2 seconds.
                        // Debug , path might change on release.
                        if (OnlineGameCreationMenuExistingConsoleCheckBox.IsChecked == false)
                        {
                            Process.Start(Directory.GetParent(Assembly.GetExecutingAssembly().Location) + "\\MinesweeperHostConsole.py"); // May be disabled to run the scrip manually in debug mode
                            ipAddr = IPAddress.Parse("127.0.0.1");
                        }
                        else
                        {
                            ipAddr = IPAddress.Parse(OnlineGameCreationMenuIpInput.Text);
                        }
                        myListener = new TcpListener(ipAddr, 22222);
                        myListener.Start();
                    }
                    catch (Exception)
                    {
                        MessageBox.Show("Could not find server file MinesweeperHostConsole.py", "Lethal error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    
                    // Attempt to connect with the client
                    Socket initialSocket = myListener.AcceptSocket();

                    // Send game propeties to script
                    initialSocket.Send(asciiEnc.GetBytes(rawGamePropeties));

                    // Input buffer is 32
                    byte[] binDataIn = new byte[32];
                    // Recieve final message from script
                    int k = initialSocket.Receive(binDataIn);
                    if (k == 0)
                        throw new Exception("Connection lost");

                    // Reads the token response from the host console and stores 
                    hostToken = asciiEnc.GetString(binDataIn, 0, k);

                    // Upon closing the While loop, closes and cleansup
                    // the socket and the tcpListener.
                    initialSocket.Close();
                    myListener.Stop();
                    #endregion

                    #region Stable connection
                    stableSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    // Gets an object from an IP
                    IPAddress hostAddress = IPAddress.Parse(OnlineGameCreationMenuIpInput.Text);
                    // Gets the endpoint the socket can connect to and work with.
                    IPEndPoint hostEndPoint = new IPEndPoint(hostAddress, int.Parse(OnlineGameCreationMenuPortInput.Text));

                    // Attempts connecting into the server
                    stableSocket.Connect(hostEndPoint);
                    isConnected = true;

                    // Sends the host token for verification
                    stableSocket.Send(asciiEnc.GetBytes(hostToken), SocketFlags.None);
                    // length 1 = true, length 2 = false.
                    isHost = stableSocket.Receive(new byte[2], 0, 2, SocketFlags.None) == 1;
                    hostToken = "null";
                    ChangeWindowPage(3, out windowPage);
                    #endregion
                }
                catch (SocketException)
                {
                    MessageBox.Show("Could not open a match in the given adress! Adress is either already in use or is invalid.", "Hosting match failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OnlineGamePreviewQuitMatchButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isHost && isConnected)
            {
                // Display a confirmation box when attempting to quit match
                if (MessageBox.Show("Are you sure you want to quit the match?", "Quit?", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    QuitMatch();
                else
                    return;
            }
            else if (isConnected)
            {
                // Display a confirmation box when attempting to quit match
                if (MessageBox.Show("Are you sure you want to close the match?", "Close match?", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    QuitMatch();
                else
                    return;
            }
            else
            {
                ChangeWindowPage(1, out windowPage);
            }

            isConnected = false;
        }

        private int CountFlags(Dictionary<int, int> FlaggedTiles)
        {
            int output = 0;
            foreach (int value in FlaggedTiles.Values)
            {
                output += value;
            }
            return output;
        }

        private void OnlineGamePreviewUpdateTimer_Tick(object sender, EventArgs e)
        {
            lock (_lockObject)
            {
                if (isConnected)
                {
                    stableSocket.Send(asciiEnc.GetBytes("CALL:UPDATE_PREVIEW"), SocketFlags.None);

                    string[] GamePreviewUpdateData = HandleServerInput(stableSocket).Split('\n');

                      if (GamePreviewUpdateData[0] != "NULL")
                    {
                        OnlineGamePreviewHostDisplayerLabel.Content = $"Host: {isHost}";
                        OnlineGamePreviewStartGameButton.IsEnabled = isHost && int.Parse(GamePreviewUpdateData[GamePreviewUpdateData.Length - 1]) > 1;

                        OnlineGamePreviewMinigameDisplayerLabel.Content = MinigameAliaeses[int.Parse(GamePreviewUpdateData[2])];
                        OnlineGamePreviewPlayerCountLabel.Content = GamePreviewUpdateData[GamePreviewUpdateData.Length - 1];
                        PlayersInGame = int.Parse(GamePreviewUpdateData[GamePreviewUpdateData.Length - 1]);

                        GameWidth = int.Parse(GamePreviewUpdateData[3]);
                        GameHeight = int.Parse(GamePreviewUpdateData[4]);
                        GameMines = int.Parse(GamePreviewUpdateData[5]);

                        OnlineGame = (byte)(int.Parse(GamePreviewUpdateData[2]) + 1);
                    }
                }
                else
                {
                    throw new Exception("Socket lost! How did you even get here?");
                }
            }
        }

        private void MatchConnectionMenuConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateEndPointInput(MatchConnectionMenuIpInput.Text, MatchConnectionMenuPortInput.Text))
            {
                stableSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                // Gets an object from an IP
                IPAddress hostAddress = IPAddress.Parse(MatchConnectionMenuIpInput.Text);
                // Gets the endpoint the socket can connect to and work with.
                IPEndPoint hostEndPoint = new IPEndPoint(hostAddress, int.Parse(OnlineGameCreationMenuPortInput.Text));

                try
                {
                    // Attempts connecting into the server
                    stableSocket.Connect(hostEndPoint);

                    string connectionAcceptance = HandleServerInput(stableSocket);
                    if (connectionAcceptance == "GAME_ALREADY_STARTED")
                    {
                        stableSocket.Close();
                        MessageBox.Show("Connection to server refused: Game already started!", "Connection failed!", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Sends the host token for verification
                    stableSocket.Send(asciiEnc.GetBytes(hostToken), SocketFlags.None);
                    // length 1 = true, length 2 = false.
                    isHost = stableSocket.Receive(new byte[2], 0, 2, SocketFlags.None) == 1;
                    hostToken = "null";

                    // Swap to the game preview page
                    ChangeWindowPage(3, out windowPage);
                    isConnected = true;
                }
                catch (SocketException)
                {
                    MessageBox.Show($"Could not connect to given Ip:Port: {hostEndPoint}\nCheck your inputs and try again.", "Connection failed!", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    
        private bool ValidateEndPointInput(string AdressInput, string PortInput)
        {
            if (IPAddress.TryParse(AdressInput, out _) && ContainsDigitsOnly(PortInput))
                if (int.Parse(PortInput) >= 0 && int.Parse(PortInput) <= 65535)
                    return true;

            return false;
        }

        private void OnlineGamePreviewStartGameButton_Click(object sender, RoutedEventArgs e)
        {
            if (isConnected)
            {
                OnlineGamePreviewUpdateTimer.Stop();

                stableSocket.Send(asciiEnc.GetBytes("CALL:START_GAME"), SocketFlags.None);

                if (HandleServerInput(stableSocket) != "NULL")
                {
                    OnlineGamePreviewUpdateTimer.Start();
                }
            }
            else
            {
                throw new Exception("Socket lost! How did you even get here?");
            }
        }

        /// <summary>
        /// Setups the start of online games
        /// Does specific actions depending on what game
        /// </summary>
        /// <param name="InitiateCall">The call command from the server that made this function run.
        /// Said call string contains important parameters</param>
        private void InitiateOnlineGame(byte gameToInitiate, ref bool flag)
        {
            if (flag)
                return;

            ChangeWindowPage(gameToInitiate, out windowPage);

            // Coop game initiation
            if (windowPage == 201)
            {
                GenerateNewGamePlane();
            }
            else
                throw new NotImplementedException();

            flag = true;
        }

        /// <summary>
        /// Submits an action to to the server in the coop gamemode
        /// </summary>
        /// <param name="Action">One of three actios: LeftClick, RightClick, RightAndLeftClick</param>
        /// <param name="TileNumber">The number of the tile to perform said action onto</param>
        private void CoopGameSubmitAction(string Action, int TileNumber)
        {
            lock (_lockObject)
            {
                stableSocket.Send(asciiEnc.GetBytes("CALL:COOP_VOTE\n" + Action + "\n" + TileNumber.ToString()), SocketFlags.None);
                string response = HandleServerInput(stableSocket);
                // Debug , semi-console
                if (response == "Vote registered")
                {
                    VotedTile = TileNumber;
                }
            }
        }

        // Starts recieving data from the server and handles it's logic
        private string HandleServerInput(Socket socket)
        {
            byte[] rawServerInput = new byte[131072];
            Debug.WriteLine(">>> RECIEVING <<<");
            int k = socket.Receive(rawServerInput, 0, 131072, SocketFlags.None);
            string[] serverInput = asciiEnc.GetString(rawServerInput, 0, k).Split('\n');
            Debug.Write("&&& (SERVER_INPUT): ");
            for (int i = 0; i < serverInput.Length; i++)
            {
                if (i == serverInput.Length - 1)
                    Debug.Write(serverInput[i] + " || ");
                else
                    Debug.Write("\n");
            }

            if (serverInput[0].Substring(0, 5) == "CALL:")
            {
                serverInput[0] = serverInput[0].Substring(5);

                if (serverInput[0] == "START_GAME")
                {
                    // Skips the START_GAME call from the server if it has not gotten at least one game preview update.
                    if (ValidateCorePropeties(true))
                    {
                        OnlineGamePreviewUpdateTimer.Stop();
                        stableSocket.Send(asciiEnc.GetBytes("DATA:STARTING_GAME"), SocketFlags.None);
                        InitiateOnlineGame(OnlineGame, ref OnlineGameInitiated);
                    }
                    else
                        stableSocket.Send(asciiEnc.GetBytes("DATA:GAME_NOT_READY"), SocketFlags.None);
                }
                else if (serverInput[0] == "QUIT")
                {
                    // Stop the update timer
                    OnlineGamePreviewUpdateTimer.Stop();

                    // Go back to the connect page
                    ChangeWindowPage(1, out windowPage);

                    if (OnlineGameInitiated)
                    {
                        DestroyGamePlane();
                        OnlineGameInitiated = false;
                    }

                    try
                    {
                        // Call leave command to the server...
                        stableSocket.Send(asciiEnc.GetBytes("CALL:QUIT"), SocketFlags.None);

                        stableSocket.Close();
                    }
                    catch (Exception) { }

                    isConnected = false;
                }
                else if (serverInput[0] == "KICK")
                {
                    throw new NotImplementedException(string.Join("\n", serverInput));
                }
                else if (serverInput[0] == "RESTART_GAME")
                {
                    if (OnlineGame == 201)
                    {
                        OldCoopTotalVotes = NewCoopTotalVotes;
                        NewCoopTotalVotes = new List<int>();
                        RestartGame();
                        CoopTagTiles();
                    }
                    else if (OnlineGame == 0)
                        throw new Exception("Server gave CALL:RESTART_GAME but OnlineGame was 0");
                    else
                        throw new NotImplementedException();
                }
                else if (serverInput[0] == "VIRTUAL_CLICK")
                {
                    Debug.WriteLine(serverInput);

                    if (serverInput[1] == "LeftClick")
                        VirtualLeftClick(int.Parse(serverInput[2]));
                    else if (serverInput[1] == "RightClick")
                        VirtualRightClick(int.Parse(serverInput[2]));
                    else if (serverInput[1] == "RightAndLeftClick")
                        VirtualRightAndLeftClick(int.Parse(serverInput[2]));
                    else
                        throw new FormatException("Invalid virtual input: " + serverInput[1]);

                    if (isHost)
                    {
                        #region Give server the game plane
                        string output = "";

                        foreach (int key in GameTileMap.Keys)
                        {
                            output += key.ToString() + "," + GameTileMap[key].ToString() + ",";
                        }
                        output = output.Trim(',');
                        output += "\n";

                        foreach (int key in TileDiscoveryState.Keys)
                        {
                            output += key.ToString() + "," + TileDiscoveryState[key].ToString() + ",";
                        }
                        output = output.Trim(',');
                        output += "\n";

                        foreach (int key in FlaggedTiles.Keys)
                        {
                            output += key.ToString() + "," + FlaggedTiles[key].ToString() + ",";
                        }
                        output = output.Trim(',');

                        stableSocket.Send(asciiEnc.GetBytes(output), SocketFlags.None);
                        #endregion
                    }
                    else
                    {
                        stableSocket.Send(asciiEnc.GetBytes("DATA:Virtual key confirmed"), SocketFlags.None);
                    }

                    VotedTile = 0;
                    OldCoopTotalVotes = NewCoopTotalVotes;
                    NewCoopTotalVotes = new List<int>();
                    CoopTagTiles();
                }
                else if (serverInput[0] == "UPDATE_GAME_PLANE")
                {
                    GameTileMap = new Dictionary<int, int>();
                    TileDiscoveryState = new Dictionary<int, bool>();
                    FlaggedTiles = new Dictionary<int, int>();


                    // A dictionary in argument form looks like a list. When reading, every odd numbered value is a key while every even numbered value is a value.

                    // GameTileMap
                    int currentKey = 0;
                    foreach (string item in serverInput[1].Split(','))
                    {
                        if (currentKey == 0)
                        {
                            currentKey = int.Parse(item);
                        }
                        else
                        {
                            GameTileMap.Add(currentKey, int.Parse(item));
                            currentKey = 0;
                        }
                    }

                    // TileDiscoveryState
                    currentKey = 0;
                    foreach (string item in serverInput[2].Split(','))
                    {
                        if (currentKey == 0)
                        {
                            currentKey = int.Parse(item);
                        }
                        else
                        {
                            TileDiscoveryState.Add(currentKey, bool.Parse(item));
                            currentKey = 0;
                        }
                    }

                    // FlaggedTiles
                    currentKey = 0;
                    foreach (string item in serverInput[3].Split(','))
                    {
                        if (currentKey == 0)
                        {
                            currentKey = int.Parse(item);
                        }
                        else
                        {
                            FlaggedTiles.Add(currentKey, int.Parse(item));
                            currentKey = 0;
                        }
                    }

                    // Debug gamemines
                    MinesweeperFlagCounter.Text = (GameMines - CountFlags(FlaggedTiles)).ToString();
                    OldCoopTotalVotes = NewCoopTotalVotes;
                    NewCoopTotalVotes = new List<int>();
                    VotedTile = 0;

                    CoopTagTiles();
                    UpdateGamePlaneVisually();
                    MinesweeperGameClockTimer.Start();
                }
                else
                {
                    // Debug
                    MessageBox.Show(serverInput[0], "Bad server call || " + isHost.ToString(), MessageBoxButton.OK, MessageBoxImage.Warning); ;
                    Debug.WriteLine(">>>>INVALID CALL DISPOSED: " + string.Join("\n", serverInput) + "<<<<");
                }

                return "NULL";
            }
            else if (serverInput[0].Substring(0, 5) == "DATA:")
            {
                return string.Join("\n", serverInput).Substring(5);
            }
            else
            {
                MessageBox.Show("DATA GOTTEN FROM SERVER USES INVALID PROTOCOL: " + string.Join(" ", serverInput), "INVALID DATA PROTOCOL", MessageBoxButton.OK, MessageBoxImage.Error);
                throw new FormatException("Invalid input protocol not CALL or DATA: " + serverInput);
            }
        }

        private void UpdateGamePlaneVisually()
        {
            for (int i = 1; i <= GameHeight * GameWidth; i++)
            {
                ColorCodeTile(GetTileFromNumber(i));
            }
        }

        private void StacksGameCreationWeightInputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (StacksGameCreationWeightInputTextBox.Text != "" && StacksGameCreationWeightInputTextBox.Text.All(c => char.IsDigit(c)) && StacksGameCreationWeightSelectionComboBox.SelectedIndex > 0)
            {
                StacksGameWeightList[StacksGameCreationWeightSelectionComboBox.SelectedIndex - 1] = int.Parse(StacksGameCreationWeightInputTextBox.Text.ToString());
            }

            string newRawInput = "";
            for (int i = 0; i < StacksGameWeightList.Count; i++)
            {
                newRawInput += StacksGameWeightList[i];
                if (i != StacksGameWeightList.Count - 1)
                   newRawInput +=  ";";
            }
            StacksGameCreationRawInputTextBox.Text = newRawInput;
            //StacksGameCreationRaw
        }

        private void StacksGameCreationWeightSelectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StacksGameCreationWeightSelectionComboBox.SelectedIndex != 0)
            {
                StacksGameCreationWeightInputTextBox.IsReadOnly = false;
                StacksGameCreationWeightInputTextBox.Text = StacksGameWeightList[StacksGameCreationWeightSelectionComboBox.SelectedIndex - 1].ToString();
            }
            else
            {
                if (StacksGameCreationWeightInputTextBox != null)
                {
                    StacksGameCreationWeightInputTextBox.Text = SumIntList(StacksGameWeightList, 0, StacksGameWeightList.Count).ToString();
                    StacksGameCreationWeightInputTextBox.IsReadOnly = true;
                }
            }
        }

        private void StacksGameCreationWeightInputTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            StacksGameCreationWeightInputTextBox.Text = SumIntList(StacksGameWeightList, 0, StacksGameWeightList.Count).ToString();
        }

        private void StacksGameCreationSetAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (StacksGameCreationWeightInputTextBox.Text != "" && StacksGameCreationWeightInputTextBox.Text.All(c => char.IsDigit(c)))
            {
                StacksGameWeightList = new List<int>();

                for (int i = 0; i < 10; i++)
                {
                    StacksGameWeightList.Add(int.Parse(StacksGameCreationWeightInputTextBox.Text));
                }

                if (StacksGameCreationWeightSelectionComboBox.SelectedIndex != 0)
                {
                    StacksGameCreationWeightInputTextBox.IsReadOnly = false;
                    StacksGameCreationWeightInputTextBox.Text = StacksGameWeightList[StacksGameCreationWeightSelectionComboBox.SelectedIndex - 1].ToString();
                }
                else
                {
                    if (StacksGameCreationWeightInputTextBox != null)
                    {
                        StacksGameCreationWeightInputTextBox.Text = SumIntList(StacksGameWeightList, 0, StacksGameWeightList.Count).ToString();
                        StacksGameCreationWeightInputTextBox.IsReadOnly = true;
                    }
                }
                    
                string newRawInput = "";
                for (int i = 0; i < StacksGameWeightList.Count; i++)
                {
                    newRawInput += StacksGameWeightList[i];
                    if (i != StacksGameWeightList.Count - 1)
                        newRawInput += ";";
                }
                StacksGameCreationRawInputTextBox.Text = newRawInput;
            }
        }

        private void StacksGameCreationSetDefaultButton_Click(object sender, RoutedEventArgs e)
        {
            StacksGameWeightList = new List<int> { 10, 25, 20, 15, 10, 9, 5, 3, 2, 1 };

            if (StacksGameCreationWeightSelectionComboBox.SelectedIndex != 0)
            {
                StacksGameCreationWeightInputTextBox.IsReadOnly = false;
                StacksGameCreationWeightInputTextBox.Text = StacksGameWeightList[StacksGameCreationWeightSelectionComboBox.SelectedIndex - 1].ToString();
            }
            else
            {
                if (StacksGameCreationWeightInputTextBox != null)
                {
                    StacksGameCreationWeightInputTextBox.Text = SumIntList(StacksGameWeightList, 0, StacksGameWeightList.Count).ToString();
                    StacksGameCreationWeightInputTextBox.IsReadOnly = true;
                }
            }

            string newRawInput = "";
            for (int i = 0; i < StacksGameWeightList.Count; i++)
            {
                newRawInput += StacksGameWeightList[i];
                if (i != StacksGameWeightList.Count - 1)
                    newRawInput += ";";
            }
            StacksGameCreationRawInputTextBox.Text = newRawInput;
        }

        private void StacksGameCreationSetRawInputButton_Click(object sender, RoutedEventArgs e)
        {
            if (StacksGameCreationRawInputTextBox.Text != "")
            {
                if (StacksGameCreationRawInputTextBox.Text.Split(';').Length == 10)
                {
                    List<int> newWeightList = new List<int>();
                    foreach (string element in StacksGameCreationRawInputTextBox.Text.Split(';'))
                    {
                        if (element.All(c => char.IsDigit(c)))
                        {
                            newWeightList.Add(int.Parse(element));
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (newWeightList.Count == 10)
                    {
                        StacksGameWeightList = newWeightList;

                        if (StacksGameCreationWeightSelectionComboBox.SelectedIndex != 0)
                        {
                            StacksGameCreationWeightInputTextBox.IsReadOnly = false;
                            StacksGameCreationWeightInputTextBox.Text = StacksGameWeightList[StacksGameCreationWeightSelectionComboBox.SelectedIndex - 1].ToString();
                        }
                        else
                        {
                            if (StacksGameCreationWeightInputTextBox != null)
                            {
                                StacksGameCreationWeightInputTextBox.Text = SumIntList(StacksGameWeightList, 0, StacksGameWeightList.Count).ToString();
                                StacksGameCreationWeightInputTextBox.IsReadOnly = true;
                            }
                        }

                        return;
                    }
                }
            }

            MessageBox.Show("Invalid raw input", "None lethal error", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {

        }
    }
}

// List of possible events: Neighbor reveal, Tile reveal, Tile flag, Restart game.

/// Do to list:
/// ( ~ means mostly)
/// Create basic GUI - DONE
/// Create a functional timer - DONE
/// Create a basic tile plane - DONE
/// Create a flag counter/flag system - DONE
/// Create the actual game plane - DONE
/// Create a restart button which allows for new games - DONE
/// Create a lose situation - DONE
/// Create a win situation - DONE
/// Create a back button that takes the player back to the main menu - DONE
/// Create more intuitve controls using keyboard/mouse keys - DONE
/// Create a scaleable GUI, a GUI that scales depending on the window size that keeps it's proportions
/// to support normally sized screens that aren't an ultrawide. Or actually make the GUI properly - DONE~
/// 
/// Finish the offline vanilla game - DONE
/// 
/// Create a MatchConnection menu that allows the user to connect to a game, or host an online game by a given set of gamerules and game choices - DONE~
/// Create a fully functional Co-Op Minesweeper gamemode - DONE
/// Create a fully functional Stacks gamemode - DONE
/// Create a fully functional Battle Royale Minesweeper gamemode - NIP

/// Minor issues (Sorted by priority):
/// Certain text needs to be replaced by an image
/// The restart button's image is poorly placed.
/// Abruptly closing the host console is likely to cause all clients to crash