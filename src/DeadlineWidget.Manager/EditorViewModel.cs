namespace DeadlineWidget.Manager;

public sealed class EditorViewModel : ObservableObject
{
    private string _title = string.Empty;
    private DateTime? _dueDate;
    private string _hour = string.Empty;
    private string _minute = string.Empty;
    private string? _validationMessage;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public DateTime? DueDate
    {
        get => _dueDate;
        set => SetProperty(ref _dueDate, value);
    }

    public string Hour
    {
        get => _hour;
        set => SetProperty(ref _hour, value);
    }

    public string Minute
    {
        get => _minute;
        set => SetProperty(ref _minute, value);
    }

    public string? ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public bool TryBuild(out string title, out DateTime dueAt)
    {
        title = Title.Trim();
        dueAt = default;

        if (title.Length == 0)
        {
            ValidationMessage = "标题不能为空。";
            return false;
        }

        if (DueDate is null)
        {
            ValidationMessage = "请选择有效日期。";
            return false;
        }

        if (!int.TryParse(Hour, out var hour) || hour is < 0 or > 23)
        {
            ValidationMessage = "小时必须是 0 到 23。";
            return false;
        }

        var minuteText = Minute.Trim();
        var minute = 0;
        if (minuteText.Length > 0 && (!int.TryParse(minuteText, out minute) || minute is < 0 or > 59))
        {
            ValidationMessage = "分钟必须留空或是 0 到 59。";
            return false;
        }

        var date = DueDate.Value;
        try
        {
            dueAt = new DateTime(date.Year, date.Month, date.Day, hour, minute, 0, DateTimeKind.Local);
        }
        catch (ArgumentOutOfRangeException)
        {
            ValidationMessage = "请选择有效日期。";
            return false;
        }

        ValidationMessage = null;
        return true;
    }

    public void BeginNew(DateTime defaultDate)
    {
        Title = string.Empty;
        DueDate = defaultDate.Date;
        Hour = string.Empty;
        Minute = string.Empty;
        ValidationMessage = null;
    }

    public void BeginEdit(string title, DateTime dueAt)
    {
        Title = title;
        DueDate = dueAt.Date;
        Hour = dueAt.Hour.ToString("00");
        Minute = dueAt.Minute == 0 ? string.Empty : dueAt.Minute.ToString("00");
        ValidationMessage = null;
    }
}
