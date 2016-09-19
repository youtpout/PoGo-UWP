﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml.Navigation;
using Newtonsoft.Json;
using PokemonGo_UWP.Entities;
using PokemonGo_UWP.Utils;
using PokemonGo_UWP.Views;
using POGOProtos.Data;
using POGOProtos.Inventory;
using Template10.Mvvm;
using Template10.Services.NavigationService;
using PokemonGo_UWP.Utils.Extensions;
using Windows.UI.Xaml.Media.Animation;
using Google.Protobuf;

namespace PokemonGo_UWP.ViewModels
{
    public class PokemonInventoryPageViewModel : ViewModelBase
    {
        #region Game Management Vars

        private int _lastVisibleIndex;

        public int LastVisibleIndex
        {
            get { return Utilities.EnsureRange(_lastVisibleIndex, 0, PokemonInventory.Count-1); }
            set { _lastVisibleIndex = value; }
        }

        public Action ResetView;

        #endregion

        #region Lifecycle Handlers

        /// <summary>
        /// </summary>
        /// <param name="parameter"></param>
        /// <param name="mode"></param>
        /// <param name="suspensionState"></param>
        /// <returns></returns>
        public override async Task OnNavigatedToAsync(object parameter, NavigationMode mode,
            IDictionary<string, object> suspensionState)
        {
            if (suspensionState.Any())
            {
                // Recovering the state
                PokemonInventory = JsonConvert.DeserializeObject<ObservableCollection<PokemonDataWrapper>>((string)suspensionState[nameof(PokemonInventory)]);
                EggsInventory = JsonConvert.DeserializeObject<ObservableCollection<PokemonDataWrapper>>((string)suspensionState[nameof(EggsInventory)]);
                PlayerProfile = new PlayerData();
                PlayerProfile.MergeFrom(ByteString.FromBase64((string)suspensionState[nameof(PlayerProfile)]).CreateCodedInput());
                RaisePropertyChanged(() => PlayerProfile);
            }
            else
            {
                // Navigating from game page, so we need to actually load the inventory
                // The sorting mode is directly bound to the settings
                PokemonInventory = new ObservableCollection<PokemonDataWrapper>(GameClient.PokemonsInventory
                    .Select(pokemonData => new PokemonDataWrapper(pokemonData))
                    .SortBySortingmode(CurrentPokemonSortingMode));

                RaisePropertyChanged(() => PokemonInventory);

                var unincubatedEggs = GameClient.EggsInventory.Where(o => string.IsNullOrEmpty(o.EggIncubatorId))
                                                              .OrderBy(c => c.EggKmWalkedTarget);
                var incubatedEggs = GameClient.EggsInventory.Where(o => !string.IsNullOrEmpty(o.EggIncubatorId))
                                                              .OrderBy(c => c.EggKmWalkedTarget);
                EggsInventory.Clear();
                // advancedrei: I have verified this is the sort order in the game.
                foreach (var incubatedEgg in incubatedEggs)
                {
                    var incubatorData = GameClient.UsedIncubatorsInventory.FirstOrDefault(incubator => incubator.Id == incubatedEgg.EggIncubatorId);
                    EggsInventory.Add(new IncubatedEggDataWrapper(incubatorData, GameClient.PlayerStats.KmWalked, incubatedEgg));
                }

                foreach (var pokemonData in unincubatedEggs)
                {
                    EggsInventory.Add(new PokemonDataWrapper(pokemonData));
                }

                if(mode == NavigationMode.Back)
                    ResetView?.Invoke();

                PlayerProfile = GameClient.PlayerProfile;
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Save state before navigating
        /// </summary>
        /// <param name="suspensionState"></param>
        /// <param name="suspending"></param>
        /// <returns></returns>
        public override async Task OnNavigatedFromAsync(IDictionary<string, object> suspensionState, bool suspending)
        {
            if (suspending)
            {
                suspensionState[nameof(PokemonInventory)] = JsonConvert.SerializeObject(PokemonInventory);
                suspensionState[nameof(EggsInventory)] = JsonConvert.SerializeObject(EggsInventory);
            }
            await Task.CompletedTask;
        }

        public override async Task OnNavigatingFromAsync(NavigatingEventArgs args)
        {
            args.Cancel = false;
            await Task.CompletedTask;
        }

        #endregion

        #region Bindable Game Vars

        /// <summary>
        /// Player's profile, we use it just for the maximum ammount of pokemon
        /// </summary>
        private PlayerData _playerProfile;
        public PlayerData PlayerProfile
        {
            get { return _playerProfile; }
            set { Set(ref _playerProfile, value); }
        }

        /// <summary>
        /// Sorting mode for current Pokemon view
        /// </summary>
        public PokemonSortingModes CurrentPokemonSortingMode
        {
            get { return SettingsService.Instance.PokemonSortingMode; }
            set
            {
                SettingsService.Instance.PokemonSortingMode = value;
                RaisePropertyChanged(nameof(CurrentPokemonSortingMode));

                // When this changes we need to sort the collection again
                UpdateSorting();
            }
        }

        /// <summary>
        /// Reference to Pokemon inventory
        /// </summary>
        public ObservableCollection<PokemonDataWrapper> PokemonInventory { get; private set; } =
            new ObservableCollection<PokemonDataWrapper>();

        /// <summary>
        /// Reference to Eggs inventory
        /// </summary>
        public ObservableCollection<PokemonDataWrapper> EggsInventory { get; private set; } =
            new ObservableCollection<PokemonDataWrapper>();

        /// <summary>
        /// Reference to Incubators inventory
        /// </summary>
        public ObservableCollection<EggIncubator> IncubatorsInventory => GameClient.FreeIncubatorsInventory;

        #endregion

        #region Game Logic

        #region Shared Logic

        private DelegateCommand _returnToGameScreen;

        /// <summary>
        /// Going back to map page
        /// </summary>
        public DelegateCommand ReturnToGameScreen
            =>
                _returnToGameScreen ??
                (_returnToGameScreen =
                    new DelegateCommand(() => { NavigationService.Navigate(typeof(GameMapPage), GameMapNavigationModes.PokemonUpdate); }, () => true));

        #endregion

        #region Pokemon Inventory Handling

        /// <summary>
        /// Show sorting overlay for pokemon inventory
        /// </summary>
        private DelegateCommand<PokemonDataWrapper> _showPokemonSortingMenuCommand;
        public DelegateCommand<PokemonDataWrapper> ShowPokemonSortingMenuCommand => _showPokemonSortingMenuCommand ?? (_showPokemonSortingMenuCommand = new DelegateCommand<PokemonDataWrapper>((selectedPokemon) =>
        {

        }));

        private void UpdateSorting()
        {
            PokemonInventory =
                new ObservableCollection<PokemonDataWrapper>(PokemonInventory.SortBySortingmode(CurrentPokemonSortingMode));

            RaisePropertyChanged(() => PokemonInventory);
        }

        #endregion

        #region Pokemon Detail

        /// <summary>
        /// Navigate to the detail page for the selected pokemon
        /// </summary>
        private DelegateCommand<PokemonDataWrapper> _gotoPokemonDetailCommand;
        public DelegateCommand<PokemonDataWrapper> GotoPokemonDetailCommand => _gotoPokemonDetailCommand ?? (_gotoPokemonDetailCommand = new DelegateCommand<PokemonDataWrapper>((selectedPokemon) => 
        {
            NavigationService.Navigate(typeof(PokemonDetailPage), new SelectedPokemonNavModel()
            {
                SelectedPokemonId = selectedPokemon.Id.ToString(),
                SortingMode = CurrentPokemonSortingMode,
                ViewMode = PokemonDetailPageViewMode.Normal
            }, new SuppressNavigationTransitionInfo());
        }));

        /// <summary>
        /// Navigate to detail page for the selected egg
        /// </summary>
        private DelegateCommand<PokemonDataWrapper> _gotoEggDetailCommand;
        public DelegateCommand<PokemonDataWrapper> GotoEggDetailCommand => _gotoEggDetailCommand ?? (_gotoEggDetailCommand =  new DelegateCommand<PokemonDataWrapper>((selectedEgg) =>
        {
            NavigationService.Navigate(typeof(EggDetailPage), selectedEgg.Id.ToString(), new SuppressNavigationTransitionInfo());
        }));

        #endregion

        #endregion
    }
}
