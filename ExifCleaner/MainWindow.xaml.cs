using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MetadataExtractor;

namespace ExifCleaner
{
    // Pomocná třída, která umožňuje editaci hodnot v DataGridu
    public class EditableMetadataItem
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public partial class MainWindow : Window
    {
        private string? _currentImagePath;
        private BitmapImage? _currentBitmap;

        // Změna na ObservableCollection s editovatelnou třídou
        private readonly ObservableCollection<EditableMetadataItem> _metaItems = new();

        public MainWindow()
        {
            InitializeComponent();
            BtnOpen.Click += BtnOpen_Click;
            BtnClean.Click += BtnClean_Click;
            MetadataGrid.ItemsSource = _metaItems;
        }

        #region NAVIGACE MEZI OBRAZOVKAMI

        private void BtnModeSingle_Click(object sender, RoutedEventArgs e)
        {
            GridHub.Visibility = Visibility.Collapsed;
            GridSingle.Visibility = Visibility.Visible;
            StatusText.Text = "Režim: Úprava jednotlivého obrázku";
        }

        private void BtnModeBatch_Click(object sender, RoutedEventArgs e)
        {
            GridHub.Visibility = Visibility.Collapsed;
            GridBatch.Visibility = Visibility.Visible;
            StatusText.Text = "Režim: Hromadné zpracování složky";
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            GridSingle.Visibility = Visibility.Collapsed;
            GridBatch.Visibility = Visibility.Collapsed;
            GridHub.Visibility = Visibility.Visible;
            StatusText.Text = "Připraven";
        }

        #endregion

        #region LOGIKA PRO JEDNOTLIVÝ OBRÁZEK (SINGLE MODE)

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Vyber obrázek",
                Filter = "Obrázky|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|Všechny soubory|*.*",
                Multiselect = false
            };

            if (ofd.ShowDialog() == true)
            {
                try
                {
                    LoadImage(ofd.FileName);
                    LoadAndShowMetadata(ofd.FileName);
                    StatusText.Text = $"Načteno: {System.IO.Path.GetFileName(ofd.FileName)}";
                }
                catch (Exception ex)
                {
                    StatusText.Text = "Chyba při načítání souboru.";
                    MessageBox.Show(this, ex.Message, "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LoadImage(string path)
        {
            var bi = new BitmapImage();
            using (var fs = File.OpenRead(path))
            {
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                bi.StreamSource = fs;
                bi.EndInit();
                bi.Freeze();
            }

            _currentImagePath = path;
            _currentBitmap = bi;
            ImagePreview.Source = bi;
        }

        private void LoadAndShowMetadata(string path)
        {
            _metaItems.Clear();
            try
            {
                var directories = ImageMetadataReader.ReadMetadata(path);
                foreach (var dir in directories)
                {
                    foreach (var tag in dir.Tags)
                    {
                        _metaItems.Add(new EditableMetadataItem { Key = tag.Name, Value = tag.Description ?? "" });
                    }
                }

                if (_metaItems.Count == 0)
                    _metaItems.Add(new EditableMetadataItem { Key = "Info", Value = "Nebyla nalezena žádná metadata." });
            }
            catch (Exception ex)
            {
                _metaItems.Add(new EditableMetadataItem { Key = "Chyba", Value = ex.Message });
            }
        }

        private void BtnClean_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBitmap == null || _currentImagePath == null)
            {
                MessageBox.Show(this, "Nejprve otevři obrázek.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                BtnClean.IsEnabled = false;
                StatusText.Text = "Probíhá čištění…";

                BitmapSource src = _currentBitmap;
                if (ChkSingleNoise.IsChecked == true)
                {
                    src = AddSubtleNoise(src);
                }

                string ext = System.IO.Path.GetExtension(_currentImagePath).ToLowerInvariant();
                string outPath = GetCleanOutputPath(_currentImagePath);

                SaveBitmapWithoutMetadata(src, outPath, ext);

                StatusText.Text = $"Uloženo: {System.IO.Path.GetFileName(outPath)}";
                LoadImage(outPath);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Čištění selhalo.";
                MessageBox.Show(this, ex.Message, "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnClean.IsEnabled = true;
            }
        }

        private static void SaveBitmapWithoutMetadata(BitmapSource source, string targetPath, string extension)
        {
            BitmapSource forSave = source;
            BitmapEncoder encoder = extension switch
            {
                ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
                ".png" => new PngBitmapEncoder(),
                ".bmp" => new BmpBitmapEncoder(),
                ".tif" or ".tiff" => new TiffBitmapEncoder(),
                _ => new PngBitmapEncoder()
            };

            if (encoder is JpegBitmapEncoder && forSave.Format != PixelFormats.Bgr24)
            {
                var conv = new FormatConvertedBitmap(forSave, PixelFormats.Bgr24, null, 0);
                conv.Freeze();
                forSave = conv;
            }

            var cleanFrame = BitmapFrame.Create(forSave, null, null, null);
            encoder.Frames.Add(cleanFrame);

            using (var fs = File.Create(targetPath))
            {
                encoder.Save(fs);
            }
        }

        private static string GetCleanOutputPath(string inputPath)
        {
            var dir = Path.GetDirectoryName(inputPath)!;
            var file = Path.GetFileNameWithoutExtension(inputPath);
            var ext = Path.GetExtension(inputPath);
            string candidate = Path.Combine(dir, $"{file}_clean{ext}");

            int i = 2;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(dir, $"{file}_clean({i}){ext}");
                i++;
            }
            return candidate;
        }

        #endregion

        #region LOGIKA PRO HROMADNÉ ČIŠTĚNÍ (BATCH MODE)

        private void BtnSelectSource_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Vyber zdrojovou složku s obrázky"
            };

            if (dialog.ShowDialog() == true)
            {
                TxtSourceFolder.Text = dialog.FolderName;
                UpdateDefaultTargetFolder();
            }
        }

        private void BtnSelectTarget_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Vyber cílovou složku pro očištěné obrázky"
            };

            if (dialog.ShowDialog() == true)
            {
                TxtTargetFolder.Text = dialog.FolderName;
                ChkDefaultTarget.IsChecked = false;
            }
        }

        private void ChkDefaultTarget_Changed(object sender, RoutedEventArgs e)
        {
            if (IsInitialized)
            {
                UpdateDefaultTargetFolder();
            }
        }

        private void UpdateDefaultTargetFolder()
        {
            if (ChkDefaultTarget.IsChecked == true && !string.IsNullOrEmpty(TxtSourceFolder.Text))
            {
                TxtTargetFolder.Text = Path.Combine(TxtSourceFolder.Text, "clean");
                TxtTargetFolder.IsReadOnly = true;
                BtnSelectTarget.IsEnabled = false;
            }
            else
            {
                TxtTargetFolder.IsReadOnly = false;
                BtnSelectTarget.IsEnabled = true;
            }
        }

        private void CmbNamingMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsInitialized) return;

            if (CmbNamingMode.SelectedIndex == 0) // Ponechat původní
            {
                TxtNamingAffix.Visibility = Visibility.Collapsed;
            }
            else
            {
                TxtNamingAffix.Visibility = Visibility.Visible;
                TxtNamingAffix.Text = CmbNamingMode.SelectedIndex == 1 ? "_clean" : "clean_";
            }
        }

        private async void BtnStartBatch_Click(object sender, RoutedEventArgs e)
        {
            string sourceDir = TxtSourceFolder.Text;
            string targetDir = TxtTargetFolder.Text;

            // OPRAVA CS0104: Použití plné cesty System.IO.Directory
            if (string.IsNullOrEmpty(sourceDir) || !System.IO.Directory.Exists(sourceDir))
            {
                MessageBox.Show(this, "Vyberte platnou zdrojovou složku.", "Upozornění", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // OPRAVA CS0104: Použití plné cesty System.IO.Directory
            string[] files = System.IO.Directory.GetFiles(sourceDir, "*.*", SearchOption.TopDirectoryOnly);
            string[] imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

            string[] filesToProcess = Array.FindAll(files, f => {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                return Array.Exists(imageExtensions, extItem => extItem == ext);
            });

            if (filesToProcess.Length == 0)
            {
                MessageBox.Show(this, "Ve zdrojové složce nebyly nalezeny žádné podporované obrázky.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            BtnStartBatch.IsEnabled = false;
            BtnBatchBack.IsEnabled = false;
            GridProgress.Visibility = Visibility.Visible;
            BatchProgressBar.Maximum = filesToProcess.Length;
            BatchProgressBar.Value = 0;

            bool addNoise = ChkBatchNoise.IsChecked == true;
            int namingMode = CmbNamingMode.SelectedIndex;
            string affix = TxtNamingAffix.Text;

            try
            {
                // OPRAVA CS0104: Použití plné cesty System.IO.Directory
                if (!System.IO.Directory.Exists(targetDir))
                {
                    System.IO.Directory.CreateDirectory(targetDir);
                }

                int processedCount = 0;

                await Task.Run(() =>
                {
                    foreach (string file in filesToProcess)
                    {
                        try
                        {
                            string fileName = Path.GetFileNameWithoutExtension(file);
                            string ext = Path.GetExtension(file).ToLowerInvariant();

                            string newFileName = namingMode switch
                            {
                                1 => $"{fileName}{affix}{ext}", // Suffix
                                2 => $"{affix}{fileName}{ext}", // Prefix
                                _ => $"{fileName}{ext}"         // Ponechat
                            };

                            string outPath = Path.Combine(targetDir, newFileName);

                            var bi = new BitmapImage();
                            using (var fs = File.OpenRead(file))
                            {
                                bi.BeginInit();
                                bi.CacheOption = BitmapCacheOption.OnLoad;
                                bi.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                                bi.StreamSource = fs;
                                bi.EndInit();
                                bi.Freeze();
                            }

                            BitmapSource src = bi;
                            if (addNoise)
                            {
                                src = AddSubtleNoise(src);
                            }

                            SaveBitmapWithoutMetadata(src, outPath, ext);
                        }
                        catch
                        {
                            // Ignorování vadného souboru
                        }

                        processedCount++;

                        Dispatcher.Invoke(() =>
                        {
                            BatchProgressBar.Value = processedCount;
                            TxtProgressStatus.Text = $"Zpracovávám snímek {processedCount} z {filesToProcess.Length}...";
                            StatusText.Text = $"Hromadné čištění: {processedCount}/{filesToProcess.Length}";
                        });
                    }
                });

                MessageBox.Show(this, $"Hromadné čištění dokončeno!\nÚspěšně zpracováno souborů: {processedCount}", "Hotovo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Během hromadného čištění došlo k chybě: {ex.Message}", "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnStartBatch.IsEnabled = true;
                BtnBatchBack.IsEnabled = true;
                GridProgress.Visibility = Visibility.Collapsed;
                StatusText.Text = "Hromadné zpracování dokončeno.";
            }
        }

        #endregion

        #region ALGORITMUS PRO PŘIDÁNÍ ŠUMU (ANONYMIZACE)

        private static BitmapSource AddSubtleNoise(BitmapSource source)
        {
            if (source.Format != PixelFormats.Bgra32)
            {
                var conv = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                conv.Freeze();
                source = conv;
            }

            var wb = new WriteableBitmap(source);
            int width = wb.PixelWidth;
            int height = wb.PixelHeight;
            int bpp = wb.Format.BitsPerPixel;
            int stride = (width * bpp + 7) / 8;

            var buffer = new byte[height * stride];
            wb.CopyPixels(buffer, stride, 0);

            var rnd = Random.Shared;

            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int i = row + x * 4; // BGRA hlavička

                    buffer[i + 0] = (byte)((buffer[i + 0] & 0xFE) | rnd.Next(2)); // B
                    buffer[i + 1] = (byte)((buffer[i + 1] & 0xFE) | rnd.Next(2)); // G
                    buffer[i + 2] = (byte)((buffer[i + 2] & 0xFE) | rnd.Next(2)); // R
                }
            }

            wb.WritePixels(new Int32Rect(0, 0, width, height), buffer, stride, 0);
            wb.Freeze();
            return wb;
        }

        #endregion
    }
}