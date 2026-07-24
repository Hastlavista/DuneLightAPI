namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>Zajednički query parametri za sve popise u katalogu.</summary>
public class PagedRequest
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 200;

    private int _page = 1;
    private int _pageSize = DefaultPageSize;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? DefaultPageSize : System.Math.Min(value, MaxPageSize);
    }

    public string Search { get; set; }
    public bool? IsActive { get; set; }
}
