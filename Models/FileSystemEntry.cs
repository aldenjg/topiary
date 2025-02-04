using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Collections.Generic;

namespace Topiary.Models
{
    public class FileSystemEntry : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        private long _size;
        public long Size
        {
            get => _size;
            set
            {
                _size = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SizeDisplay));
            }
        }
        public DateTime LastAccessTime { get; set; }
        public DateTime CreationTime { get; set; }
        public bool IsDirectory { get; set; }
        private ObservableCollection<FileSystemEntry> _children;
        public ObservableCollection<FileSystemEntry> Children
        {
            get => _children;
            private set
            {
                _children = value;
                OnPropertyChanged();
            }
        }
        public double PercentageOfParent { get; set; }
        public string SizeDisplay => FormatSize(Size);
        public FileSystemEntry Parent { get; set; }

        public FileSystemEntry()
        {
            _children = new ObservableCollection<FileSystemEntry>();
        }

        private string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        public void CalculatePercentages()
        {
            if (Children.Count == 0) return;
            
            long totalSize = Size;
            foreach (var child in Children)
            {
                child.PercentageOfParent = totalSize > 0 
                    ? (double)child.Size / totalSize * 100 
                    : 0;
                child.CalculatePercentages();
            }
        }

        public void AddChildren(IEnumerable<FileSystemEntry> childEntries)
        {
            // Ensure UI updates happen on dispatcher thread
            if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                    Children.AddRange(childEntries)
                );
            }
            else
            {
                Children.AddRange(childEntries);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}