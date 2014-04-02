using BlackjackBot.Shared;
using Microsoft.AspNet.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace BlackjackBot.Bot
{
	public class CustomBot : IBot
	{
        /// <summary>
        /// A unique key required to run your bot. The key can be obtained by registering.
        /// </summary>
        private const string Key = "KEY_GOES_HERE -  http://blackjackbotserver.azurewebsites.net/";
        public CustomBot()
        {
            //make sure to register your bot to get a valid name and key!
            BotName = "BOTNAME_GOES_HERE - http://blackjackbotserver.azurewebsites.net/";
        }

        /// <summary>
        /// A unique name for the bot
        /// </summary>
        public string BotName { get; private set; }

		//SignalR Hub proxy
		private IHubProxy _hubProxy;

        /// <summary>
        /// This event will fire when the Game State is updated either with a new game, a move, or a game is completed
        /// </summary>
		public event EventHandler<GameState> GameStateUpdated;

		/// <summary>
		/// A list of cards that have been played. 
		/// </summary>
		readonly List<Card> _cardsPlayed = new List<Card>();

		/// <summary>
		/// Will be set to true when the GameSeries completed event is fired and set to false when a series is starting. 
		/// </summary>
		public bool IsGameSeriesCompleted { get; private set; }

		/// <summary>
		/// This sets up your signalRbot
		/// </summary>
		/// <param name="url"></param>
		/// <returns></returns>
		public async Task InitializeAsync(string url)
		{
			//Sets up connection and events from SignalR server
			HubConnection connection = new HubConnection(url);
			_hubProxy = connection.CreateHubProxy("BlackjackHub");

			//Receive when Game Starts
			_hubProxy.On<GameState>("gameStarted", GameStarted);

			//Receive when it's your turn
			_hubProxy.On<GameState>("playerMove", async gameState => { await PlayerMoveAsync(gameState); });

			//Receive when game is completed
			_hubProxy.On<GameState>("gameCompleted", GameCompleted);

			//Receive when game series is completed
			_hubProxy.On<GameState>("gameSeriesCompleted", GameSeriesCompleted);

			//Receive when cards are shuffled
			_hubProxy.On("deckShuffled", DeckShuffled);

			//Receive when an error is received
			_hubProxy.On<string>("errorReceived", ErrorReceived);
			_hubProxy.On<string>("informationReceived", InformationReceived);

			//setup SignalR
			await connection.Start();            
		}

		/* START METHODS TO START BLACKJACK  */
		public async Task<bool> JoinMultiPlayerGameQueueAsync()
		{
			bool added = await _hubProxy.Invoke<bool>("JoinMultiPlayerGameQueueAsync", BotName, Key);
			if(added)
				Debug.WriteLine(BotName + " joined multiplayer game queue"); 
			else
				Debug.WriteLine(BotName + " is already in the queue or used an invalid name/key combination");
			return added;
		}

		public async Task<bool> StartSoloGameSeriesAsync(int numberOfGames)
		{
			//validate # of games is between 1 and 10 (this is done on the server too)
			if (numberOfGames <= 0)
				return false;

			if (numberOfGames > 10)
				numberOfGames = 10;

			bool started = await _hubProxy.Invoke<bool>("StartSoloGameSeriesAsync", BotName, Key, numberOfGames);
			if(started)
				Debug.WriteLine("SoloGameSeries Started");
			else
				Debug.WriteLine("Failed to start solo game");
			return started;
		}
        /* END METHODS TO PLAY BLACKJACK */


        /* START METHODS FOR YOU TO CUSTOMIZE */
		private void GameStarted(GameState gameState)
		{
            //Fire the event to share the new GameState
			OnGameStateUpdated(gameState);

			//no data returned
			if (gameState.Me == null)
				return;

			// balance must be positive
			if (gameState.Me.Balance <= 0)            
				return;             

			try
			{
				//Bet 10%
				decimal amountToBet = gameState.Me.Balance / 10;
                
				if (amountToBet >= GameState.MinimumBet && amountToBet <= GameState.MaximumBet)
				{
					//place your bet
					_hubProxy.Invoke<decimal>("PlaceBet", amountToBet);
					Debug.WriteLine("Bot:" + gameState.Me.Name + ", Placed Bet:" + amountToBet);
				}
				else
				{
					Debug.WriteLine("Bot:" + gameState.Me.Name + ", Bet must be between:" + GameState.MinimumBet + " and " + GameState.MaximumBet);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine("no bet placed, exception:" + ex); 
			}    
		}

		private async Task PlayerMoveAsync(GameState gameState)
		{
            //Fire the event to share the new GameState
			OnGameStateUpdated(gameState);

			if (gameState.Me.Hand == null)
			{
				Debug.WriteLine(gameState.Me.Name + " has null hand");
				return; 
			}

            //the move we will be sending
			Move moveToSend;

			//Get the best hand without busting
			int total = gameState.Me.Hand.GetBestHand();

			//Stay on higher than 16
			if (total > 16)
			{
				moveToSend = Move.Stand;
			}
			// Double Down on 10 or 11
			else if ((total== 10 || total == 11) && gameState.Me.CanDoubleDown)
			{
				moveToSend = Move.DoubleDown;
			}
			//Otherwise hit
			else
			{
				moveToSend = Move.Hit;
			}
			Debug.WriteLine(BotName + " made move:" + moveToSend); 

			//Send our move
			await _hubProxy.Invoke<Move>("PlayerMoveAsync", moveToSend);
		}

        private void DeckShuffled()
        {
            Debug.WriteLine("DeckShuffled fired");
            //clear out my cards; 
            _cardsPlayed.Clear();
        }

		private void GameCompleted(GameState gameState)
		{
            //Fire the event to share the new GameState
			OnGameStateUpdated(gameState);

			Debug.WriteLine("End of Game\n");           
			Debug.WriteLine(gameState.DisplayResults()); 

			//loop through all players and add the used cards to List
			foreach (var player in gameState.AllPlayers)
			{
				foreach (var card in player.Hand.Cards)
				{
					_cardsPlayed.Add(card);
				}
			}
		}

		private void GameSeriesCompleted(GameState gameState)
		{
			OnGameStateUpdated(gameState);

			IsGameSeriesCompleted = true;
            PlayerState winner = gameState.AllPlayers.OrderByDescending(p => p.Balance).FirstOrDefault(); 
			Debug.WriteLine("End of Game Series, winner:" + winner.Name + ", Balance:" + winner.Balance);
		}

        private void InformationReceived(string infoMessage)
        {
            Debug.WriteLine("Info: " + infoMessage);
        }

        private void ErrorReceived(string errorMessage)
        {
            Debug.WriteLine("** Error: " + errorMessage);
        }

        /* END METHODS FOR YOU TO CUSTOMIZE */

		//Use this diagnostic method to ensure that you're getting results back
		public async Task<string> CallEchoTest(string text)
		{
			//calls the service and returns the result instantly
			string result = await _hubProxy.Invoke<string>("Echo", text);
			Debug.WriteLine(result);
			return result;
		}
        //Fires the GameStateUpdated event when it receives new GameState
		private void OnGameStateUpdated(GameState gameState)
		{
			if(GameStateUpdated != null)
				GameStateUpdated(this, gameState);
		}  
	}
}
