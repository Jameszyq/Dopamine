﻿using Digimezzo.Utilities.Log;
using Digimezzo.Utilities.Settings;
using Digimezzo.Utilities.Utils;
using Dopamine.Core.Base;
using Dopamine.Core.Prism;
using Dopamine.Data;
using Dopamine.Services.Collection;
using Dopamine.Services.Dialog;
using Dopamine.Services.Entities;
using Dopamine.Services.Indexing;
using Dopamine.Services.Metadata;
using Dopamine.Services.Playback;
using Dopamine.Services.Playlist;
using Dopamine.Services.Search;
using Dopamine.Services.Utils;
using Dopamine.ViewModels.Common.Base;
using Prism.Commands;
using Prism.Events;
using Prism.Ioc;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace Dopamine.ViewModels.FullPlayer.Collection
{
    public class CollectionGenresViewModel : AlbumsViewModelBase, ISemanticZoomViewModel
    {
        private ICollectionService collectionService;
        private IIndexingService indexingService;
        private IMetadataService metadataService;
        private IDialogService dialogService;
        private IPlaybackService playbackService;
        private IPlaylistService playlistService;
        private ISearchService searchService;
        private IEventAggregator eventAggregator;
        private ObservableCollection<ISemanticZoomable> genres;
        private CollectionViewSource genresCvs;
        private IList<string> selectedGenres;
        private ObservableCollection<ISemanticZoomSelector> genresZoomSelectors;
        private bool isGenresZoomVisible;
        private long genresCount;
        private double leftPaneWidthPercent;
        private double rightPaneWidthPercent;

        public DelegateCommand<string> AddGenresToPlaylistCommand { get; set; }

        public DelegateCommand<object> SelectedGenresCommand { get; set; }

        public DelegateCommand ShowGenresZoomCommand { get; set; }

        public DelegateCommand<string> SemanticJumpCommand { get; set; }

        public DelegateCommand AddGenresToNowPlayingCommand { get; set; }

        public DelegateCommand ShuffleSelectedGenresCommand { get; set; }

        public double LeftPaneWidthPercent
        {
            get { return this.leftPaneWidthPercent; }
            set
            {
                SetProperty<double>(ref this.leftPaneWidthPercent, value);
                SettingsClient.Set<int>("ColumnWidths", "GenresLeftPaneWidthPercent", Convert.ToInt32(value));
            }
        }

        public double RightPaneWidthPercent
        {
            get { return this.rightPaneWidthPercent; }
            set
            {
                SetProperty<double>(ref this.rightPaneWidthPercent, value);
                SettingsClient.Set<int>("ColumnWidths", "GenresRightPaneWidthPercent", Convert.ToInt32(value));
            }
        }

        public ObservableCollection<ISemanticZoomable> Genres
        {
            get { return this.genres; }
            set { SetProperty<ObservableCollection<ISemanticZoomable>>(ref this.genres, value); }
        }
        ObservableCollection<ISemanticZoomable> ISemanticZoomViewModel.SemanticZoomables
        {
            get { return Genres; }
            set { Genres = value; }
        }

        public CollectionViewSource GenresCvs
        {
            get { return this.genresCvs; }
            set { SetProperty<CollectionViewSource>(ref this.genresCvs, value); }
        }

        public IList<string> SelectedGenres
        {
            get { return this.selectedGenres; }
            set { SetProperty<IList<string>>(ref this.selectedGenres, value); }
        }

        public long GenresCount
        {
            get { return this.genresCount; }
            set { SetProperty<long>(ref this.genresCount, value); }
        }

        public bool IsGenresZoomVisible
        {
            get { return this.isGenresZoomVisible; }
            set { SetProperty<bool>(ref this.isGenresZoomVisible, value); }
        }

        public ObservableCollection<ISemanticZoomSelector> GenresZoomSelectors
        {
            get { return this.genresZoomSelectors; }
            set { SetProperty<ObservableCollection<ISemanticZoomSelector>>(ref this.genresZoomSelectors, value); }
        }
        ObservableCollection<ISemanticZoomSelector> ISemanticZoomViewModel.SemanticZoomSelectors
        {
            get { return GenresZoomSelectors; }
            set { GenresZoomSelectors = value; }
        }

        public CollectionGenresViewModel(IContainerProvider container) : base(container)
        {
            // Dependency injection
            this.collectionService = container.Resolve<ICollectionService>();
            this.dialogService = container.Resolve<IDialogService>();
            this.indexingService = container.Resolve<IIndexingService>();
            this.metadataService = container.Resolve<IMetadataService>();
            this.playbackService = container.Resolve<IPlaybackService>();
            this.playlistService = container.Resolve<IPlaylistService>();
            this.searchService = container.Resolve<ISearchService>();
            this.eventAggregator = container.Resolve<IEventAggregator>();

            // Commands
            this.ToggleTrackOrderCommand = new DelegateCommand(async () => await this.ToggleTrackOrderAsync());
            this.ToggleAlbumOrderCommand = new DelegateCommand(async () => await this.ToggleAlbumOrderAsync());
            this.RemoveSelectedTracksCommand = new DelegateCommand(async () => await this.RemoveTracksFromCollectionAsync(this.SelectedTracks), () => !this.IsIndexing);
            this.RemoveSelectedTracksFromDiskCommand = new DelegateCommand(async () => await this.RemoveTracksFromDiskAsync(this.SelectedTracks), () => !this.IsIndexing);
            this.AddGenresToPlaylistCommand = new DelegateCommand<string>(async (playlistName) => await this.AddGenresToPlaylistAsync(this.SelectedGenres, playlistName));
            this.SelectedGenresCommand = new DelegateCommand<object>(async (parameter) => await this.SelectedGenresHandlerAsync(parameter));
            this.ShowGenresZoomCommand = new DelegateCommand(async () => await this.ShowSemanticZoomAsync());
            this.AddGenresToNowPlayingCommand = new DelegateCommand(async () => await this.AddGenresToNowPlayingAsync(this.SelectedGenres));
            this.ShuffleSelectedGenresCommand = new DelegateCommand(async () => await this.playbackService.EnqueueGenresAsync(this.SelectedGenres, true, false));

            this.SemanticJumpCommand = new DelegateCommand<string>((header) =>
            {
                this.HideSemanticZoom();
                this.eventAggregator.GetEvent<PerformSemanticJump>().Publish(new Tuple<string, string>("Genres", header));
            });

            // Settings
            SettingsClient.SettingChanged += async (_, e) =>
            {
                if (SettingsClient.IsSettingChanged(e, "Behaviour", "EnableRating"))
                {
                    this.EnableRating = (bool)e.SettingValue;
                    this.SetTrackOrder("GenresTrackOrder");
                    await this.GetTracksAsync(null, this.SelectedGenres, this.SelectedAlbumKeys, this.TrackOrder);
                }

                if (SettingsClient.IsSettingChanged(e, "Behaviour", "EnableLove"))
                {
                    this.EnableLove = (bool)e.SettingValue;
                    this.SetTrackOrder("GenresTrackOrder");
                    await this.GetTracksAsync(null, this.SelectedGenres, this.SelectedAlbumKeys, this.TrackOrder);
                }
            };

            // PubSub Events
            this.eventAggregator.GetEvent<ShellMouseUp>().Subscribe((_) => this.IsGenresZoomVisible = false);

            // Events
            this.metadataService.MetadataChanged += MetadataChangedHandlerAsync;
            this.indexingService.AlbumArtworkAdded += async (_, e) => await this.RefreshAlbumArtworkAsync(e.AlbumKeys);

            // Set the initial AlbumOrder
            this.AlbumOrder = (AlbumOrder)SettingsClient.Get<int>("Ordering", "GenresAlbumOrder");

            // Set the initial TrackOrder
            this.SetTrackOrder("GenresTrackOrder");

            // Set width of the panels
            this.LeftPaneWidthPercent = SettingsClient.Get<int>("ColumnWidths", "GenresLeftPaneWidthPercent");
            this.RightPaneWidthPercent = SettingsClient.Get<int>("ColumnWidths", "GenresRightPaneWidthPercent");

            // Cover size
            this.SetCoversizeAsync((CoverSizeType)SettingsClient.Get<int>("CoverSizes", "GenresCoverSize"));
        }

        public async Task ShowSemanticZoomAsync()
        {
            this.GenresZoomSelectors = await SemanticZoomUtils.UpdateSemanticZoomSelectors(this.GenresCvs.View);

            this.IsGenresZoomVisible = true;
        }

        public void HideSemanticZoom()
        {
            this.IsGenresZoomVisible = false;
        }

        public void UpdateSemanticZoomHeaders()
        {
            string previousHeader = string.Empty;

            foreach (GenreViewModel gvm in this.GenresCvs.View)
            {
                if (string.IsNullOrEmpty(previousHeader) || !gvm.Header.Equals(previousHeader))
                {
                    previousHeader = gvm.Header;
                    gvm.IsHeader = true;
                }
                else
                {
                    gvm.IsHeader = false;
                }
            }
        }

        private async void MetadataChangedHandlerAsync(MetadataChangedEventArgs e)
        {
            if (e.IsArtworkChanged) await this.RefreshAlbumArtworkAsync();
            if (e.IsGenreChanged) await this.GetGenresAsync();
            // TODO if (e.IsGenreChanged | e.IsAlbumChanged) await this.GetAlbumsAsync(null, this.SelectedGenres, this.AlbumOrder);
            // TODO if (e.IsGenreChanged | e.IsAlbumChanged | e.IsTrackChanged) await this.GetTracksAsync(null, this.SelectedGenres, this.SelectedAlbumIds, this.TrackOrder);
        }

        private async Task GetGenresAsync()
        {
            try
            {
                // Get the genres
                var genreViewModels = new ObservableCollection<GenreViewModel>(await this.collectionService.GetAllGenresAsync());

                // Unbind to improve UI performance
                if (this.GenresCvs != null) this.GenresCvs.Filter -= new FilterEventHandler(GenresCvs_Filter);
                this.Genres = null;
                this.GenresCvs = null;

                // Populate ObservableCollection
                this.Genres = new ObservableCollection<ISemanticZoomable>(genreViewModels);
            }
            catch (Exception ex)
            {
                LogClient.Error("An error occurred while getting Genres. Exception: {0}", ex.Message);

                // Failed getting Genres. Create empty ObservableCollection.
                this.Genres = new ObservableCollection<ISemanticZoomable>();
            }

            // Populate CollectionViewSource
            this.GenresCvs = new CollectionViewSource { Source = this.Genres };
            this.GenresCvs.Filter += new FilterEventHandler(GenresCvs_Filter);

            // Update count
            this.GenresCount = this.GenresCvs.View.Cast<ISemanticZoomable>().Count();

            // Update Semantic Zoom Headers
            this.UpdateSemanticZoomHeaders();
        }

        private async Task SelectedGenresHandlerAsync(object parameter)
        {
            if (parameter != null)
            {
                this.SelectedGenres = new List<string>();

                foreach (GenreViewModel item in (IList)parameter)
                {
                    this.SelectedGenres.Add(item.GenreName);
                }
            }

            // Don't reload the lists when updating Metadata. MetadataChangedHandlerAsync handles that.
            if (this.metadataService.IsUpdatingDatabaseMetadata) return;

            await this.GetAlbumsAsync(null, this.SelectedGenres, this.AlbumOrder);
            this.SetTrackOrder("GenresTrackOrder");
            await this.GetTracksAsync(null, this.SelectedGenres, this.SelectedAlbumKeys, this.TrackOrder);
        }

        private async Task AddGenresToPlaylistAsync(IList<string> genres, string playlistName)
        {
            AddPlaylistResult addPlaylistResult = AddPlaylistResult.Success; // Default Success

            // If no playlist is provided, first create one.
            if (playlistName == null)
            {
                var responseText = ResourceUtils.GetString("Language_New_Playlist");

                if (this.dialogService.ShowInputDialog(
                    0xea37,
                    16,
                    ResourceUtils.GetString("Language_New_Playlist"),
                    ResourceUtils.GetString("Language_Enter_Name_For_New_Playlist"),
                    ResourceUtils.GetString("Language_Ok"),
                    ResourceUtils.GetString("Language_Cancel"),
                    ref responseText))
                {
                    playlistName = responseText;
                    addPlaylistResult = await this.playlistService.AddPlaylistAsync(playlistName);
                }
            }

            // If playlist name is still null, the user clicked cancel on the previous dialog. Stop here.
            if (playlistName == null) return;

            // Verify if the playlist was added
            switch (addPlaylistResult)
            {
                case AddPlaylistResult.Success:
                case AddPlaylistResult.Duplicate:
                    // Add items to playlist
                    AddTracksToPlaylistResult result = await this.playlistService.AddGenresToPlaylistAsync(genres, playlistName);

                    if (result == AddTracksToPlaylistResult.Error)
                    {
                        this.dialogService.ShowNotification(0xe711, 16, ResourceUtils.GetString("Language_Error"), ResourceUtils.GetString("Language_Error_Adding_Songs_To_Playlist").Replace("{playlistname}", "\"" + playlistName + "\""), ResourceUtils.GetString("Language_Ok"), true, ResourceUtils.GetString("Language_Log_File"));
                    }
                    break;
                case AddPlaylistResult.Error:
                    this.dialogService.ShowNotification(
                        0xe711,
                        16,
                        ResourceUtils.GetString("Language_Error"),
                        ResourceUtils.GetString("Language_Error_Adding_Playlist"),
                        ResourceUtils.GetString("Language_Ok"),
                        true,
                        ResourceUtils.GetString("Language_Log_File"));
                    break;
                case AddPlaylistResult.Blank:
                    this.dialogService.ShowNotification(
                        0xe711,
                        16,
                        ResourceUtils.GetString("Language_Error"),
                        ResourceUtils.GetString("Language_Provide_Playlist_Name"),
                        ResourceUtils.GetString("Language_Ok"),
                        false,
                        string.Empty);
                    break;
                default:
                    // Never happens
                    break;
            }
        }

        private async Task AddGenresToNowPlayingAsync(IList<string> genres)
        {
            EnqueueResult result = await this.playbackService.AddGenresToQueueAsync(genres);

            if (!result.IsSuccess)
            {
                this.dialogService.ShowNotification(0xe711, 16, ResourceUtils.GetString("Language_Error"), ResourceUtils.GetString("Language_Error_Adding_Genres_To_Now_Playing"), ResourceUtils.GetString("Language_Ok"), true, ResourceUtils.GetString("Language_Log_File"));
            }
        }

        private void GenresCvs_Filter(object sender, FilterEventArgs e)
        {
            GenreViewModel gvm = e.Item as GenreViewModel;

            e.Accepted = Services.Utils.EntityUtils.FilterGenres(gvm, this.searchService.SearchText);
        }

        private async Task ToggleTrackOrderAsync()
        {
            base.ToggleTrackOrder();

            SettingsClient.Set<int>("Ordering", "GenresTrackOrder", (int)this.TrackOrder);
            await this.GetTracksCommonAsync(this.Tracks, this.TrackOrder);
        }

        private async Task ToggleAlbumOrderAsync()
        {
            base.ToggleAlbumOrder();

            SettingsClient.Set<int>("Ordering", "GenresAlbumOrder", (int)this.AlbumOrder);
            await this.GetAlbumsCommonAsync(this.Albums, this.AlbumOrder);
        }

        protected async override Task SetCoversizeAsync(CoverSizeType coverSize)
        {
            await base.SetCoversizeAsync(coverSize);
            SettingsClient.Set<int>("CoverSizes", "GenresCoverSize", (int)coverSize);
        }

        protected async override Task FillListsAsync()
        {
            await this.GetGenresAsync();
            await this.GetAlbumsAsync(null, this.SelectedGenres, this.AlbumOrder);
            await this.GetTracksAsync(null, this.SelectedGenres, this.SelectedAlbumKeys, this.TrackOrder);
        }

        protected override void FilterLists()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Genres
                if (this.GenresCvs != null)
                {
                    this.GenresCvs.View.Refresh();
                    this.GenresCount = this.GenresCvs.View.Cast<ISemanticZoomable>().Count();
                    this.UpdateSemanticZoomHeaders();
                }
            });

            base.FilterLists();
        }

        protected async override Task SelectedAlbumsHandlerAsync(object parameter)
        {
            await base.SelectedAlbumsHandlerAsync(parameter);

            this.SetTrackOrder("GenresTrackOrder");
            await this.GetTracksAsync(null, this.SelectedGenres, this.SelectedAlbumKeys, this.TrackOrder);
        }

        protected override void RefreshLanguage()
        {
            this.UpdateAlbumOrderText(this.AlbumOrder);
            this.UpdateTrackOrderText(this.TrackOrder);
            base.RefreshLanguage();
        }
    }
}
