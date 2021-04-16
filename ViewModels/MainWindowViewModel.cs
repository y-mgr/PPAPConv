using GongSolutions.Wpf.DragDrop;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace PPAPConv.ViewModels
{
    enum BackupPolicy
    {
        Rename,
        Preserve
    }

    class MainWindowViewModel : IDropTarget, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public BackupPolicy[] Policies { get; } = new BackupPolicy[] { BackupPolicy.Rename, BackupPolicy.Preserve };

        public ObservableCollection<ZipEntry> EntryList { get; } = new ObservableCollection<ZipEntry>();

        private string sourceName;
        public string SourceName
        {
            get => sourceName;
            set
            {
                if (sourceName != value)
                {
                    sourceName = value;
                    NotifyPropertyChanged(nameof(SourceName));
                }
            }
        }

        private int policyIndex = 1;
        public int PolicyIndex
        {
            get => policyIndex;
            set
            {
                if (policyIndex != value)
                {
                    policyIndex = value;
                    NotifyPropertyChanged(nameof(PolicyIndex));
                }
            }
        }

        private string keyString = String.Empty;
        public string KeyString
        {
            get => keyString;
            set
            {
                if (keyString != value)
                {
                    keyString = value;
                    if (string.IsNullOrEmpty(value))
                    {
                        throw new ArgumentException();
                    }
                    NotifyPropertyChanged(nameof(KeyString));
                }
            }
        }

        public ICommand SelectCommand { get; }
        public ICommand ConvertCommand { get; }
        public ICommand ExitCommand { get; }

        private bool? isEncrypted;
        public bool? IsEncrypted
        {
            get => isEncrypted;
            set
            {
                if (isEncrypted != value)
                {
                    isEncrypted = value;
                    NotifyPropertyChanged(nameof(IsEncrypted));
                }
            }
        }

        private class ActionCommand : ICommand
        {
            private readonly Action<object> Handler;
            public ActionCommand(Action<object> handler)
            {
                Handler = handler;
            }

            public event EventHandler CanExecuteChanged;

            public bool CanExecute(object parameter) => true;

            public void Execute(object parameter)
            {
                Handler?.Invoke(parameter);
            }
        }

        public MainWindowViewModel()
        {
            SelectCommand = new ActionCommand(SelectZipAction);
            ConvertCommand = new ActionCommand(DoConvertAsync);
            ExitCommand = new ActionCommand((_)=>{ Application.Current.MainWindow.Close(); });
            PropertyChanged += OnProyertyChanged;
        }

        private void NotifyPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void OnProyertyChanged(object sender, PropertyChangedEventArgs args)
        {
            switch (args.PropertyName)
            {
                case nameof(SourceName):
                    UpdateEntryList(SourceName);
                    break;
                default:
                    break;
            }
        }

        public void DragOver(IDropInfo dropInfo)
        {
            var files = ((DataObject)dropInfo.Data).GetFileDropList().Cast<string>();
            var acceptable = files.Any(x => x.EndsWith(value: ".zip", ignoreCase: true, culture: null));
            dropInfo.Effects = acceptable ? DragDropEffects.Copy : DragDropEffects.None;
        }

        public void Drop(IDropInfo dropInfo)
        {
            var files = ((DataObject)dropInfo.Data).GetFileDropList().Cast<string>();
            SourceName = files.FirstOrDefault(x => x.EndsWith(value: ".zip", ignoreCase: true, culture: null));
        }

        private void UpdateEntryList(string zipPath)
        {
            EntryList.Clear();
            IsEncrypted = null;
            if (zipPath == null)
            {
                return;
            }
            try
            {
                using (var zipFile = new ZipFile(zipPath))
                { 
                    foreach (ZipEntry ze in zipFile)
                    {
                        EntryList.Add(ze);
                    }
                }
                IsEncrypted = EntryList.FirstOrDefault(x => x.IsFile)?.IsCrypted;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Scan Aborted", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SelectZipAction(object _)
        {
            var directory = Path.GetDirectoryName(SourceName);
            var dlg = new OpenFileDialog
            {
                Filter = "zip file(*.zip)|*.zip|all file(*.*)|*.*",
                FilterIndex = 1,
                DefaultExt = "zip",
                InitialDirectory = directory,
            };
            var result = dlg.ShowDialog();
            if (result == true)
            {
                SourceName = dlg.FileName;
            }
        }


        private async void DoConvertAsync(object _)
        {
            if (string.IsNullOrEmpty(SourceName))
            {
                MessageBox.Show("Can't start. SOURCE ZIP file must be set.", "ERROR", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (string.IsNullOrEmpty(KeyString))
            {
                MessageBox.Show("Can't start. PASSWORD must be set.", "ERROR", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                using (var zipFile = new ZipFile(SourceName))
                {
                    IsEncrypted = EntryList.FirstOrDefault(x => x.IsFile)?.IsCrypted;
                    if (IsEncrypted == null)
                    {
                        MessageBox.Show(" No file included in the ZIP archive.", "Not converted", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    var suffix = new StringBuilder(".");
                    if (IsEncrypted == true)
                    {
                        suffix.Append("decrypted");
                    }
                    else
                    {
                        suffix.Append("encrypted");
                    }
                    var outZipPath = GetSuffixedPath(SourceName, suffix.ToString());
                    try
                    {
                        using (var zofs = new ZipOutputStream(new FileStream(outZipPath, FileMode.CreateNew, FileAccess.Write)))
                        {
                            zofs.SetLevel(9);
                            if (IsEncrypted == true)
                            {
                                zipFile.Password = KeyString;
                            }
                            else
                            {
                                zofs.Password = KeyString;
                            }

                            foreach (ZipEntry ize in zipFile)
                            {
                                var oze = GetOutputZipEntry(ize);
                                zofs.PutNextEntry(oze);
                                if (ize.IsFile)
                                {
                                    var buffer = new byte[ize.Size];
                                    var offset = 0;
                                    var remained = buffer.Length;
                                    while (remained > 0)
                                    {
                                        var length = await zipFile.GetInputStream(ize).ReadAsync(buffer, offset, remained);
                                        remained -= length;
                                        offset += length;
                                    }
                                    await zofs.WriteAsync(buffer, 0, buffer.Length);
                                }
                                zofs.CloseEntry();
                                await zofs.FlushAsync();
                            }
                            zipFile.Close();
                            zofs.Finish();
                            zofs.Close();
                            if (Policies[PolicyIndex] == BackupPolicy.Rename)
                            {
                                var backupPath = GetSuffixedPath(SourceName, ".backup");
                                File.Move(SourceName, backupPath);
                                File.Move(outZipPath, SourceName);
                                outZipPath = SourceName;
                                UpdateEntryList(SourceName);
                            }
                        }
                        var openIt = MessageBox.Show(string.Format("Converted file is \"{0}\"\nDo you want open in Explorer?", outZipPath), "Completed", MessageBoxButton.OKCancel, MessageBoxImage.None);
                        if (openIt == MessageBoxResult.OK)
                        {
                            System.Diagnostics.Process.Start("explorer.exe", string.Format("/select,\"{0}\"", outZipPath));
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Aborted", MessageBoxButton.OK, MessageBoxImage.Warning);
                        File.Delete(outZipPath);
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Aborted", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private string GetSuffixedPath(string source, string suffix)
        {
            var dirName = Path.GetDirectoryName(source);
            var basename = Path.GetFileNameWithoutExtension(source);
            var ext = Path.GetExtension(source);
            var outPath = Path.Combine(dirName, string.Format("{0}{1}{2}", basename, suffix, ext));
            int count = 0;
            while (File.Exists(outPath))
            {
                count++;
                outPath = Path.Combine(dirName, string.Format("{0}{1}{2}{3}", basename, suffix, count, ext));
            }
            return outPath;
        }

        private ZipEntry GetOutputZipEntry(ZipEntry ize)
        {
            ZipEntry oze = new ZipEntry(ize.Name)
            {
                DateTime = ize.DateTime,
            };
            if (!string.IsNullOrEmpty(ize.Comment))
            {
                oze.Comment = ize.Comment;
            }
            if (ize.ExtraData != null)
            {
                oze.ExtraData = ize.ExtraData;
            }
            // invert encryption
            if (IsEncrypted == true)
            {
                oze.IsCrypted = false;
            }
            else
            {
                oze.IsCrypted = true;
            }
            return oze;
        }
    }
}
