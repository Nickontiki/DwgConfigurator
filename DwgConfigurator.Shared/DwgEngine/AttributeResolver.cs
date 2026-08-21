namespace DwgConfigurator.Shared.DwgEngine;

public static class AttributeResolver
{
    // -- 22 attributi DINAMICI (compilati dall'utente) --
    public static readonly string[] DynamicTags = new[]
    {
        "CUSTOMER1", "CUSTOMER2",
        "FINALCLIENT1", "FINALCLIENT2", "FINALCLIENT3",
        "INSTALLATIONSITE1", "INSTALLATIONSITE2", "INSTALLATIONSITE3", "INSTALLATIONSITE4",
        "ORDERTEXT",
        "DRAWINGTITLE1", "DRAWINGTITLE2", "DRAWINGTITLE3", "DRAWINGTITLE4",
        "MODEL1", "MODEL2",
        "SCALETEXT", "FORMATTEXT", "FILETEXT",
        "DRAWINGNUMBER", "BAR_CODE",
        "REV_REVISION", "REV_DATE", "REV_ISSUE", "REV_DRAWN", "REV_CHECKED",
        "APPROVED"
    };

    // -- 13 attributi FISSI CARTIGLIO (APPROVED rimosso: e' un attributo dinamico del RevisionBlock) --
    public static readonly string[] CartiglioFixedTags = new[]
    {
        "REVISION", "DATE", "ISSUE",
        "DRAWN", "CHECKED",
        "CUSTOMER", "FINALCLIENT", "INSTALLATIONSITE",
        "DRAWINGTITLE",
        "SCALE", "FORMAT", "FILE", "ORDER"
    };

    // -- 28 attributi FISSI LAYOUT (blocco LegendBlock) --
    public static readonly string[] LayoutFixedTags = new[]
    {
        "SMOKEDETECTOR", "THERMALDETECTOR", "GASDETECTOR",
        "EMERGENCYBUTTON", "FIREFIGHTINGBUTTON", "STOPENGINEPUSHBUTTON",
        "OPTICALACOUSTICPANEL", "OPTICALACOUSTICSIGNALING",
        "EMERGENCYLIGHT", "WATERPROOFLIGHT", "WATERPROOFLIGHTEMERGENCY",
        "KEYSWITCH", "PUSHBUTTON", "SINGLEPOLESWITCH",
        "TWOWAYSWITCH", "REVERSINGSWITCH",
        "GALVANIZEDFOOTBRIDGE", "JUNCTIONBOX",
        "SOCKET", "SOCKETGERMANSTANDARD", "SOCKETUNIVERSAL",
        "INTERLOCKEDSOCKET2P", "INTERLOCKEDSOCKET3P", "INTERLOCKEDSOCKET3PN",
        "THERMISTOR", "THERMOSTAT", "BUZZER", "LEGENDTITLE"
    };

    public static string[] FixedTags => CartiglioFixedTags;

    public static Dictionary<string, string> Resolve(
        Dictionary<string, string> userInput,
        Dictionary<string, string> cartiglioFixed,
        Dictionary<string, string> layoutFixed)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var tag in DynamicTags)
        {
            if (userInput.TryGetValue(tag, out var val) && !string.IsNullOrEmpty(val))
                result[tag] = val;
            else
                result[tag] = string.Empty;
        }

        foreach (var tag in CartiglioFixedTags)
        {
            // Se l'utente ha gia' inserito un valore dinamico, non sovrascrivere
            if (result.TryGetValue(tag, out var existing) && !string.IsNullOrEmpty(existing))
                continue;

            if (cartiglioFixed.TryGetValue(tag, out var val) && !string.IsNullOrEmpty(val))
                result[tag] = val;
            else if (!result.ContainsKey(tag))
                result[tag] = string.Empty;
        }

        foreach (var tag in LayoutFixedTags)
        {
            if (layoutFixed.TryGetValue(tag, out var val) && val is not null)
                result[tag] = val;
            else if (!result.ContainsKey(tag))
                result[tag] = string.Empty;
        }

        return result;
    }

    public static Dictionary<string, string> Resolve(
        Dictionary<string, string> userInput,
        Dictionary<string, string> cartiglioFixed)
    {
        return Resolve(userInput, cartiglioFixed, new Dictionary<string, string>());
    }
}
