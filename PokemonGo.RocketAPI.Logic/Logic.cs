using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using AllEnum;
using PokemonGo.RocketAPI.Enums;
using PokemonGo.RocketAPI.Exceptions;
using PokemonGo.RocketAPI.Extensions;
using PokemonGo.RocketAPI.GeneratedCode;
using PokemonGo.RocketAPI.Logic.Utils;

namespace PokemonGo.RocketAPI.Logic
{
    public class Logic
    {
        private readonly Client _client;
        private readonly ISettings _clientSettings;
        private readonly Inventory _inventory;

        public Logic(ISettings clientSettings)
        {
            _clientSettings = clientSettings;
            _client = new Client(_clientSettings);
            _inventory = new Inventory(_client);
        }

        public async Task Execute()
        {
            Logger.Write($"Starting Execute on login server: {_clientSettings.AuthType}", LogLevel.Info);

            while (true)
            {
                try
                {
                    if (_clientSettings.AuthType == AuthType.Ptc)
                        await _client.DoPtcLogin(_clientSettings.PtcUsername, _clientSettings.PtcPassword);
                    else if (_clientSettings.AuthType == AuthType.Google)
                        await _client.DoGoogleLogin();

                    await PostLoginExecute();
                }
                catch (AccessTokenExpiredException)
                {
                    Logger.Write($"Access token expired", LogLevel.Info);
                }
                await Task.Delay(10000);
            }
        }

        public async Task PostLoginExecute()
        {
            while (true)
            {
                try
                {
                    await _client.SetServer();
                    await DisplayPlayerLevelInTitle();
                    await EvolveAllPokemonWithEnoughCandy();
                    await TransferDuplicatePokemon();
                    await RecycleItems();
                    await ExecuteFarmingPokestopsAndPokemons();

                    /*
            * Example calls below
            *
            var profile = await _client.GetProfile();
            var settings = await _client.GetSettings();
            var mapObjects = await _client.GetMapObjects();
            var inventory = await _client.GetInventory();
            var pokemons = inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.Pokemon).Where(p => p != null && p?.PokemonId > 0);
            */
                }
                catch (AccessTokenExpiredException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Write($"Exception: {ex}", LogLevel.Error);
                }

                await Task.Delay(10000);
            }
        }

        public static float Perfect(PokemonData poke)
        {
 +          return ((float)(poke.IndividualAttack*2 + poke.IndividualDefense + poke.IndividualStamina) / (4.0f * 15.0f)) * 100.0f;
        }
 
        public async Task RepeatAction(int repeat, Func<Task> action)
        {
            for (int i = 0; i < repeat; i++)
                await action();
        }

        private async Task DisplayPlayerLevelInTitle()
        {
            var stats = await _inventory.GetPlayerStats();
            PlayerStats stat = stats.FirstOrDefault();
            if (stat != null)
            {
                System.Console.Title = string.Format("Player level {0:0} - ({1:0} / {2:0})",
                     +stat.Level,
                     +(stat.Experience - stat.PrevLevelXp),
                     +(stat.NextLevelXp - stat.PrevLevelXp));
            }
            await Task.Delay(5000);
        }
        
        private async Task ExecuteFarmingPokestopsAndPokemons()
        {
            var mapObjects = await _client.GetMapObjects();

            var pokeStops = mapObjects.MapCells.SelectMany(i => i.Forts).Where(i => i.Type == FortType.Checkpoint && i.CooldownCompleteTimestampMs < DateTime.UtcNow.ToUnixTime());
            pokeStops = Navigation.generatePath(pokeStops.ToList());
            foreach (var pokeStop in pokeStops)
            {
                var distance = Navigation.DistanceBetween2Coordinates(_client.CurrentLat, _client.CurrentLng, pokeStop.Latitude, pokeStop.Longitude);
                var update = await _client.UpdatePlayerLocation(pokeStop.Latitude, pokeStop.Longitude, _clientSettings.DefaultAltitude);
                var fortInfo = await _client.GetFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);
                var fortSearch = await _client.SearchFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);
                Logger.Write($"Using Pokestop: {fortInfo.Name} in {Math.Round(distance)}m distance");
                Logger.Write($"Farmed XP: {fortSearch.ExperienceAwarded}, Gems: { fortSearch.GemsAwarded}, Eggs: {fortSearch.PokemonDataEgg} Items: {StringUtils.GetSummedFriendlyNameOfItemAwardList(fortSearch.ItemsAwarded)}", LogLevel.Info);
                if (fortSearch.ExperienceAwarded > 0)
                {
                    await DisplayPlayerLevelInTitle();
                }
                await Task.Delay(1000);
                await RecycleItems();
                await ExecuteCatchAllNearbyPokemons();
                await TransferDuplicatePokemon();
            }
        }

        private async Task ExecuteCatchAllNearbyPokemons()
        {
            var mapObjects = await _client.GetMapObjects();

            var pokemons = mapObjects.MapCells.SelectMany(i => i.CatchablePokemons);

            foreach (var pokemon in pokemons)
            {
                var distance = Navigation.DistanceBetween2Coordinates(_client.CurrentLat, _client.CurrentLng, pokemon.Latitude, pokemon.Longitude);
                
                await Task.Delay(distance > 100 ? 15000 : 500);

                await _client.UpdatePlayerLocation(pokemon.Latitude, pokemon.Longitude, _clientSettings.DefaultAltitude);

                var encounter = await _client.EncounterPokemon(pokemon.EncounterId, pokemon.SpawnpointId);
                await CatchEncounter(encounter, pokemon);
            }
            await Task.Delay(15000);
        }

        private async Task CatchEncounter(EncounterResponse encounter, MapPokemon pokemon)
        {

            CatchPokemonResponse caughtPokemonResponse;
            do
            {
                if (encounter?.CaptureProbability.CaptureProbability_.First() < 0.35)
                {
                    //Throw berry if we can
                    await UseBerry(pokemon.EncounterId, pokemon.SpawnpointId);
                }

                var pokeball = await GetBestBall(encounter?.WildPokemon);
                var distance = Navigation.DistanceBetween2Coordinates(_client.CurrentLat, _client.CurrentLng, pokemon.Latitude, pokemon.Longitude);
                caughtPokemonResponse = await _client.CatchPokemon(pokemon.EncounterId, pokemon.SpawnpointId, pokemon.Latitude, pokemon.Longitude, pokeball);
                Logger.Write(caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchSuccess ? $"We caught a {pokemon.PokemonId} with CP {encounter?.WildPokemon?.PokemonData?.Cp} ({Perfect(encounter?.WildPokemon?.PokemonData).ToString("0.00")}% perfect) and CaptureProbability: {encounter?.CaptureProbability.CaptureProbability_.First()} using a {pokeball} in {Math.Round(distance)}m distance" : $"{pokemon.PokemonId} with CP {encounter?.WildPokemon?.PokemonData?.Cp} CaptureProbability: {encounter?.CaptureProbability.CaptureProbability_.First()} in {Math.Round(distance)}m distance {caughtPokemonResponse.Status} while using a {pokeball}..", LogLevel.Info);
                await Task.Delay(2000);
            }
            while (caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchMissed || caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchEscape) ;
            if (caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchSuccess)
            {
                await DisplayPlayerLevelInTitle();
            }
            
        }

        private async Task EvolveAllPokemonWithEnoughCandy()
        {
            var pokemonToEvolve = await _inventory.GetPokemonToEvolve();
            int pokemonEvolved = 0;
            foreach (var pokemon in pokemonToEvolve)
            {
                var evolvePokemonOutProto = await _client.EvolvePokemon((ulong)pokemon.Id);

                Logger.Write(   
                    evolvePokemonOutProto.Result == EvolvePokemonOut.Types.EvolvePokemonStatus.PokemonEvolvedSuccess
                    
                        ? $"Evolved {pokemon.PokemonId} successfully for {evolvePokemonOutProto.ExpAwarded} xp"
                        : $"Failed to evolve {pokemon.PokemonId}. EvolvePokemonOutProto.Result was {evolvePokemonOutProto.Result}, stopping evolving {pokemon.PokemonId}",
                    LogLevel.Info);
                    
                await Task.Delay(3000);
            }
        }

        private async Task TransferDuplicatePokemon(bool keepPokemonsThatCanEvolve = false, float keepPerfectPokemonLimit = 85.0f)
        {
            var duplicatePokemons = await _inventory.GetDuplicatePokemonToTransfer(keepPokemonsThatCanEvolve);

            foreach (var duplicatePokemon in duplicatePokemons)
            {
                var bestPokemonOfType = await _inventory.GetHighestCPofType(duplicatePokemon);
                if (Perfect(duplicatePokemon) >= keepPerfectPokemonLimit) continue;
                var transfer = await _client.TransferPokemon(duplicatePokemon.Id);
                Logger.Write($"Transfer {duplicatePokemon.PokemonId} with {duplicatePokemon.Cp} CP (Best: {bestPokemonOfType})", LogLevel.Info);
                await Task.Delay(500);
            }
            if (pokemonEvolved > 0)
            {
                await DisplayPlayerLevelInTitle();
            }
        }

        private async Task RecycleItems()
        {
            var items = await _inventory.GetItemsToRecycle(_clientSettings);

            foreach (var item in items)
            {
                var transfer = await _client.RecycleItem((AllEnum.ItemId)item.Item_, item.Count);
                Logger.Write($"Recycled {item.Count}x {(AllEnum.ItemId)item.Item_}", LogLevel.Info);
                await Task.Delay(500);
            }
        }

        private async Task<MiscEnums.Item> GetBestBall(WildPokemon pokemon)
        {
            var pokemonCp = pokemon?.PokemonData?.Cp;

            var items = await _inventory.GetItems();
            var balls = items.Where(i => (MiscEnums.Item)i.Item_ == MiscEnums.Item.ITEM_POKE_BALL
                                    || (MiscEnums.Item)i.Item_ == MiscEnums.Item.ITEM_MASTER_BALL
                                    || (MiscEnums.Item)i.Item_ == MiscEnums.Item.ITEM_ULTRA_BALL
                                    || (MiscEnums.Item)i.Item_ == MiscEnums.Item.ITEM_GREAT_BALL).GroupBy(i => ((MiscEnums.Item)i.Item_)).ToList();
                
            if (balls.Any(g => g.Key == MiscEnums.Item.ITEM_MASTER_BALL) && pokemonCp >= 1000)    
                return MiscEnums.Item.ITEM_MASTER_BALL;
                
            if (balls.Any(g => g.Key == MiscEnums.Item.ITEM_ULTRA_BALL) && pokemonCp >= 600)
                return MiscEnums.Item.ITEM_ULTRA_BALL;
                
            if (balls.Any(g => g.Key == MiscEnums.Item.ITEM_GREAT_BALL) && pokemonCp >= 350)
                return MiscEnums.Item.ITEM_GREAT_BALL;
            
            return balls.OrderBy(g => g.Key).First().Key;
        }

        public async Task UseBerry(ulong encounterId, string spawnPointId)
        {
            var inventoryBalls = await _inventory.GetItems();
            var berries = inventoryBalls.Count(p => (ItemId) p.Item_ == ItemId.ItemRazzBerry);

            if (berries == 0) return;
            
            await _client.UseCaptureItem(encounterId, AllEnum.ItemId.ItemRazzBerry, spawnPointId);
            Logger.Write($"Use Rasperry. Remaining: {berries}", LogLevel.Info);
            await Task.Delay(3000);
        }
    }
}
