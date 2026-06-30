using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GalenaActionRing.Services
{
    public class ProfileManager
    {
        private readonly List<RingProfile> _profiles = new();
        private int _selectedIndex;
        private RingProfile? _editingCopy;

        public IReadOnlyList<RingProfile> Profiles => _profiles.AsReadOnly();
        public int SelectedIndex => _selectedIndex;
        public RingProfile? EditingCopy => _editingCopy;

        public event Action? ProfilesChanged;
        public event Action<RingProfile>? ProfileLoaded;

        public void Init()
        {
            _profiles.Clear();
            var existing = ProfileService.ListProfiles();
            foreach (var name in existing)
            {
                var p = ProfileService.LoadProfile(name) ?? new RingProfile { Name = name };
                if (p.Nodes.Count > 0 && p.Nodes.All(n => n.Category == ActionCategory.Individual &&
                    n.ActionType is ActionType.MediaPlayPause or ActionType.MediaNext or
                    ActionType.MediaPrevious or ActionType.MediaSeekForward or ActionType.MediaSeekBackward))
                {
                    var fresh = ProfileService.CreateDefault();
                    fresh.Name = p.Name;
                    fresh.ProcessName = p.ProcessName;
                    p = fresh;
                }
                MigrateFolderGlyphs(p.Nodes);
                _profiles.Add(p);
            }
            if (_profiles.Count == 0)
            {
                var def = ProfileService.CreateDefault();
                ProfileService.SaveProfile(def);
                _profiles.Add(def);
            }
            _selectedIndex = 0;
            ProfilesChanged?.Invoke();
        }

        public void SelectIndex(int index)
        {
            _selectedIndex = index;
            Load();
        }

        public void Load()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count) return;
            var profile = _profiles[_selectedIndex];
            _editingCopy = profile.DeepCopy();
            ProfileLoaded?.Invoke(profile);
        }

        public string? Save()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count) return null;
            var profile = _profiles[_selectedIndex];
            if (_editingCopy == null) return null;

            var duplicate = _profiles
                .Select((p, i) => (p, i))
                .FirstOrDefault(x => x.i != _selectedIndex &&
                                     string.Equals(x.p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
            if (duplicate.p != null)
                return $"A ring named \"{profile.Name}\" already exists. Choose a different name.";

            profile.Nodes = _editingCopy.Nodes.ConvertAll(n => n.DeepCopy());
            profile.Radius = _editingCopy.Radius;

            ProfileService.SaveProfile(profile);
            return null;
        }

        public RingProfile? Add(string ringName)
        {
            if (string.IsNullOrWhiteSpace(ringName)) return null;
            if (_profiles.Any(p => string.Equals(p.Name, ringName, StringComparison.OrdinalIgnoreCase)))
                return null;

            var template = ProfileService.CreateDefault();
            var newProfile = new RingProfile
            {
                Name = ringName,
                Radius = template.Radius,
                PrimaryColor = template.PrimaryColor,
                SecondaryColor = template.SecondaryColor,
                Nodes = template.Nodes.ConvertAll(n => n.DeepCopy())
            };
            ProfileService.SaveProfile(newProfile);
            _profiles.Add(newProfile);
            _selectedIndex = _profiles.Count - 1;
            ProfilesChanged?.Invoke();
            return newProfile;
        }

        public bool CanDelete => _profiles.Count > 1;

        public void Delete()
        {
            if (_profiles.Count <= 1) return;
            if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count) return;

            var profile = _profiles[_selectedIndex];
            ProfileService.DeleteProfile(profile.Name);
            _profiles.RemoveAt(_selectedIndex);
            _selectedIndex = Math.Max(0, _selectedIndex - 1);
            ProfilesChanged?.Invoke();
        }

        public string? Rename(string newName)
        {
            if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count) return null;
            if (string.IsNullOrWhiteSpace(newName)) return "Ring name cannot be empty.";

            var profile = _profiles[_selectedIndex];
            if (_profiles.Any(p => p != profile && string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase)))
                return $"A ring named \"{newName}\" already exists. Choose a different name.";

            ProfileService.DeleteProfile(profile.Name);
            profile.Name = newName;
            ProfileService.SaveProfile(profile);
            ProfilesChanged?.Invoke();
            return null;
        }

        public void Reset()
        {
            _profiles.Clear();
            foreach (var name in ProfileService.ListProfiles())
                ProfileService.DeleteProfile(name);

            var def = ProfileService.CreateDefault();
            ProfileService.SaveProfile(def);
            _profiles.Add(def);
            _selectedIndex = 0;
            ProfilesChanged?.Invoke();
        }

        private static void MigrateFolderGlyphs(List<RingNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.ActionType == ActionType.Folder && node.Glyph == "\uE05F")
                    node.Glyph = "\uF6B5";
                if (node.Children != null)
                    MigrateFolderGlyphs(node.Children);
            }
        }
    }
}
