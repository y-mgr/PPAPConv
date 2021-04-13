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

    internal class MemorySource : IStaticDataSource
    {
        private byte[] Data;

        public MemorySource(byte[] data)
        {
            Data = data;
        }

        public Stream GetSource()
        {
            return new MemoryStream(Data);
        }
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

        private bool? IsEncrypted;

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
                        using (var zofs = ZipFile.Create(outZipPath))
                        {
                            zofs.BeginUpdate();
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
                                    zofs.Add(new MemorySource(buffer), oze);
                                }
                                else if (ize.IsDirectory)
                                {
                                    zofs.AddDirectory(oze.Name);
                                }
                            }
                            zipFile.Close();
                            zofs.CommitUpdate();
                            zofs.Close();
                            if (Policies[PolicyIndex] == BackupPolicy.Rename)
                            {
                                var backupPath = GetSuffixedPath(SourceName, ".backuup");
                                File.Move(SourceName, backupPath);
                                File.Move(outZipPath, SourceName);
                                outZipPath = SourceName;
                            }
                        }
                        MessageBox.Show(string.Format("Converted file is {0}", outZipPath), "Completed", MessageBoxButton.OK, MessageBoxImage.None);
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
                Comment = ize.Comment,
                DateTime = ize.DateTime,
                CompressionMethod = ize.CompressionMethod,
                ExtraData = ize.ExtraData,
                HostSystem = ize.HostSystem,
                IsUnicodeText = ize.IsUnicodeText,
                Flags = ize.Flags
            };
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
