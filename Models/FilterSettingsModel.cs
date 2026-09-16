using System.Collections.Generic;

namespace HoloAvalonia.Models;

public sealed class FilterSettingsModel
{
    public List<string> HiddenMembers { get; set; } = [];
    public List<string> KnownMembers { get; set; } = [];
}
