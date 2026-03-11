namespace Allow2.Models;

/// <summary>
/// Standard Allow2 activity identifiers.
/// </summary>
public enum Activity : ulong
{
    Internet = 1,
    Computer = 2,
    Gaming = 3,
    Message = 4,
    JunkFood = 5,
    Lollies = 6,
    Electricity = 7,
    ScreenTime = 8,
    Social = 9,
    PhoneTime = 10
}

/// <summary>
/// An activity to include in a permission check request.
/// </summary>
public record ActivityCheck(Activity Id, bool Log = true);
