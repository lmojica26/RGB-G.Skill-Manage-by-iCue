using GSkillCue.Bridge;
using GSkillCue.Cli;
using GSkillCue.Core;

Log.MinLevel = LogLevel.Info;

try
{
    return args switch
    {
    ["smbus", .. var rest] => Commands.SmbusScan(rest),
    ["spd", "--backup", var file, .. var r] => Commands.SpdBackup(file, r),
    ["spd", "--verify", var file, .. var r] => Commands.SpdVerify(file, r),
    ["ram", "--detect", .. var r] => Commands.RamDetect(r),
    ["ram", "--test", var effect, .. var r] => Commands.RamTest(effect, r),
    ["icue", "--list", ..] => Commands.ICueList(),
    ["icue", "--dump", var id, .. var r] => Commands.ICueDump(id, r),
    ["run", .. var r] => Commands.Run(r),
    ["--help" or "-h" or "help" or []] => Commands.Help(),
        _ => Commands.Unknown(args),
    };
}
catch (Exception ex)
{
    Log.Error(ex.Message);
    if (Environment.GetEnvironmentVariable("GSKILLCUE_DEBUG") == "1")
        Console.Error.WriteLine(ex);
    return 1;
}
