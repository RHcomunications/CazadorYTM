namespace CazadorYTM.Gui.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

public enum MainTabType
{
    Downloads,
    History,
    Player,
    Radar
}

public class MainTabItem : ObservableObject
{
    public MainTabType TabType { get; set; }
    public string Header { get; set; } = string.Empty;
    public object Content { get; set; }

    public MainTabItem(MainTabType tabType, string header, object content)
    {
        TabType = tabType;
        Header = header;
        Content = content;
    }
}
