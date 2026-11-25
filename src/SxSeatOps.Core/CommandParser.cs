using System.Text.RegularExpressions;

namespace SxSeatOps.Core;

/// <summary>Validated console command with original normalized tokens.</summary>
public sealed record ParsedCommand(string Name,IReadOnlyList<string> Arguments);
/// <summary>Result of parsing one command line.</summary>
public sealed record CommandParseResult(bool Succeeded,ParsedCommand? Command,string? Error);
/// <summary>Small, bounded command grammar parser for the SeatOps console.</summary>
public static partial class CommandParser
{
    /// <summary>Parses supported command keywords case-insensitively and rejects malformed input.</summary>
    public static CommandParseResult Parse(string? input)
    {
        if(string.IsNullOrWhiteSpace(input))return new(false,null,"Command is empty.");
        if(input.Length>BusinessRules.MaximumCommandCharacters)return new(false,null,"Command is too long.");
        string[] tokens=Tokenize(input.Trim());if(tokens.Length==0)return new(false,null,"Command is empty.");string name=tokens[0].ToUpperInvariant();
        string[] args=new string[tokens.Length-1];Array.Copy(tokens,1,args,0,args.Length);
        bool valid=name switch
        {
            "STAT" or "UNDO" or "REDO"=>args.Length==0,
            "LOGIN"=>args.Length>=2,
            "BLK"=>args.Length>=2,
            "REL"=>args.Length==1,
            "ASG" or "SWP"=>args.Length==2,
            "UPG"=>args.Length==2,
            "PROCESS"=>args.Length==1&&args[0].Equals("UPGRADES",StringComparison.OrdinalIgnoreCase),
            "VERIFY"=>args.Length<=1,
            "CLOCK"=>args.Length==1,
            "IMPORT" or "EXPORT"=>args.Length>=2,
            _=>false
        };
        if(!valid)return new(false,null,"Unknown command or invalid argument count.");
        return new(true,new ParsedCommand(name,Array.AsReadOnly(args)),null);
    }
    private static string[] Tokenize(string input)
    {
        var parts=new List<string>();var current=new System.Text.StringBuilder();bool quoted=false;
        foreach(char c in input){if(c=='"'){quoted=!quoted;continue;}if(char.IsWhiteSpace(c)&&!quoted){if(current.Length>0){parts.Add(current.ToString());current.Clear();}}else current.Append(c);}
        if(quoted)return Array.Empty<string>();if(current.Length>0)parts.Add(current.ToString());return parts.ToArray();
    }
    /// <summary>Normalizes a flight number to two letters plus four digits.</summary>
    public static bool TryNormalizeFlightNumber(string? value,out string? normalized)
    {normalized=null;if(value is null||!FlightNumberRegex().IsMatch(value))return false;var lettersBuilder=new System.Text.StringBuilder(2);var digitsBuilder=new System.Text.StringBuilder(4);foreach(char c in value){if(char.IsLetter(c))lettersBuilder.Append(c);else if(c is >= '0' and <= '9')digitsBuilder.Append(c);}string digits=digitsBuilder.ToString();if(digits.Length is <1 or >4||!int.TryParse(digits,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out int number)||number is <1 or >9999)return false;normalized=lettersBuilder.ToString().ToUpperInvariant()+number.ToString("D4",System.Globalization.CultureInfo.InvariantCulture);return true;}
    [GeneratedRegex("\\A[A-Za-z]{2}[ -]?[0-9]{1,4}\\z",RegexOptions.CultureInvariant)]private static partial Regex FlightNumberRegex();
}
