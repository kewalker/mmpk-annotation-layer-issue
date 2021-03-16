using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Portal;
using Esri.ArcGISRuntime.Tasks.Offline;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace mmpk_annotation_layer_issue
{
    public partial class MapPage : ContentPage
    {
        private Map _offlineMapArea;

        private string mmpkFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        private string OfflineMapAreaFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Offline\OfflineMap");

        public MapPage()
        {
            InitializeComponent();
            Startup();
        }

        private async void Startup()
        {
            await Task.WhenAll(Initialize(), CheckOfflineMapArea());
        }

        private async Task Initialize()
        {
            try
            {
                // Set the map
                var portal = await ArcGISPortal.CreateAsync();
                var item = await PortalItem.CreateAsync(portal, "fc76be1ab8a049edb4e659c1837bcca4");
                var map = new Map(item);
                TheMap.Map = map;

                // Copy our annotation layer mmpk into place for loading later
                using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("mmpk_annotation_layer_issue.simple_anno_mmpk.mmpk"))
                {
                    using (var file = new FileStream(GetMmpkPath(), FileMode.Create, FileAccess.Write))
                    {
                        resource.CopyTo(file);
                    }
                }

            }
            catch (Exception e)
            {
                await Application.Current.MainPage.DisplayAlert("Error", e.ToString(), "OK");
            }
        }

        private string GetMmpkPath()
        {
            return Path.Combine(mmpkFolder, "simple_anno_mmpk.mmpk");
        }

        private void OnDemandJob_ProgressChanged(object sender, EventArgs e)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                var job = (GenerateOfflineMapJob)sender;
                DownloadMapAreaStatusLabel.Text = string.Format($"Offline Map Area Download: {job.Status}, {job.Progress}%");
                DownloadMapAreaStatusLabel.TextColor = Color.Black;
            });
        }

        private void UpdateJob_ProgressChanged(object sender, EventArgs e)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                var job = (OfflineMapSyncJob)sender;
                UpdateStatusLabel.Text = string.Format($"Update Feature: {job.Status}, {job.Progress}%");
                UpdateStatusLabel.TextColor = Color.Black;
            });
        }

        private async void DownloadButton_Clicked(object sender, EventArgs e)
        {
            await DownloadOfflineMapArea();
        }

        private async void ActivateButton_Clicked(object sender, EventArgs e)
        {
            // Open the map package.
            MobileMapPackage myMapPackage = await MobileMapPackage.OpenAsync(GetMmpkPath());
            await myMapPackage.LoadAsync();

            // Get our annotation layer
            var annoLayer = myMapPackage.Maps.First().AllLayers.First();
            myMapPackage.Maps.First().OperationalLayers.Clear();
            myMapPackage.Close();

            // Add it to our offline map area
            _offlineMapArea.OperationalLayers.Add(annoLayer);
            TheMap.Map = _offlineMapArea;

            Device.BeginInvokeOnMainThread(() =>
            {
                ActivateMapAreaButton.IsEnabled = false;
                UpdateFeatureButton.IsEnabled = true;
            });
        }

        private async void UpdateFeatureButton_Clicked(object sender, EventArgs e)
        {
            // Get our point layer
            var featureLayer = (TheMap.Map.AllLayers[3] as FeatureLayer);
            var table = featureLayer.FeatureTable;
            var features = await table.QueryFeaturesAsync(new Esri.ArcGISRuntime.Data.QueryParameters { });

            // Update a feature
            var feature = features.First();
            feature.SetAttributeValue("Description", "updated");
            await table.UpdateFeatureAsync(feature);

            // Create the sync task
            var syncTask = await OfflineMapSyncTask.CreateAsync(TheMap.Map);

            // Create the job
            var syncDirection = SyncDirection.Upload;
            var job = syncTask.SyncOfflineMap(new OfflineMapSyncParameters()
            {
                RollbackOnFailure = true,
                SyncDirection = syncDirection
            });

            job.ProgressChanged += UpdateJob_ProgressChanged;

            // Await result
            var result = await job.GetResultAsync();

            try
            {
                // Here's where we'll get an error trying to access result.LayerResults.
                // It throws a cast exception.
                var error = result.LayerResults;
            }
            catch (Exception ex)
            {
                UpdateStatusLabel.TextColor = Color.Red;
                await Application.Current.MainPage.DisplayAlert("Error", ex.ToString(), "OK");
            }
        }

        private async Task CheckOfflineMapArea()
        {
            if (!Directory.Exists(OfflineMapAreaFolder))
            {
                Directory.CreateDirectory(OfflineMapAreaFolder);
            }

            if (Directory.EnumerateFiles(OfflineMapAreaFolder).Any())
            {
                try
                {
                    var mmpk = await MobileMapPackage.OpenAsync(OfflineMapAreaFolder);
                    DownloadMapAreaStatusLabel.Text = "Offline Map Area: Download complete.";
                    DownloadMapAreaStatusLabel.TextColor = Color.Green;
                    _offlineMapArea = mmpk.Maps.First();

                    Device.BeginInvokeOnMainThread(() =>
                    {
                        OfflineMapAreaButton.IsEnabled = false;
                        ActivateMapAreaButton.IsEnabled = true;
                    });
                }
                catch (Exception)
                {
                    Directory.Delete(OfflineMapAreaFolder, true);
                }
            }
        }

        private async Task DownloadOfflineMapArea()
        {
            var downloadTask = await OfflineMapTask.CreateAsync(TheMap.Map);
            var parameters = await downloadTask.CreateDefaultGenerateOfflineMapParametersAsync(TheMap.GetCurrentViewpoint(ViewpointType.BoundingGeometry).TargetGeometry);
            parameters.AttachmentSyncDirection = AttachmentSyncDirection.None;
            parameters.ReturnLayerAttachmentOption = ReturnLayerAttachmentOption.None;
            var job = downloadTask.GenerateOfflineMap(parameters, OfflineMapAreaFolder);
            job.ProgressChanged += OnDemandJob_ProgressChanged;
            var result = await job.GetResultAsync();
            if (!result.HasErrors)
            {
                _offlineMapArea = result.OfflineMap;
                Device.BeginInvokeOnMainThread(() =>
                {
                    OfflineMapAreaButton.IsEnabled = false;
                    ActivateMapAreaButton.IsEnabled = true;
                });
            }
        }
    }
}
