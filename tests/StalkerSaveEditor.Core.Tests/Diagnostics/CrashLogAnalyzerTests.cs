using StalkerSaveEditor.Core.Diagnostics;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Diagnostics;

public sealed class CrashLogAnalyzerTests
{
    [Fact]
    public void Parses_xray_fatal_error_fields_without_claiming_a_known_fix()
    {
        const string log = """
            [error]Expression    : assertion failed
            [error]Function      : CScriptEngine::lua_error
            [error]File          : E:\stalker\gamedata\scripts\task_manager.script
            [error]Line          : 428
            [error]Description   : attempt to index a nil value
            """;

        var modified = new DateTimeOffset(2026, 9, 28, 12, 34, 56, TimeSpan.Zero);
        var result = CrashLogAnalyzer.Analyze(log, game: "Clear Sky", fileLastWriteTimeUtc: modified);

        Assert.Equal(CrashLogKind.FatalError, result.Kind);
        Assert.Equal("Clear Sky", result.Game);
        Assert.Equal(@"E:\stalker\gamedata\scripts\task_manager.script", result.File);
        Assert.Equal(428, result.Line);
        Assert.Equal(modified, result.FileLastWriteTimeUtc);
        Assert.Contains("assertion failed", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.KnownIssueId);
    }

    [Fact]
    public void Parses_lua_stack_frame_and_labels_unknown_signature_as_unmatched()
    {
        const string log = """
            [LUA] SCRIPT ERROR
            attempt to call a nil value
            stack traceback:
            gamedata\scripts\actor_binder.script:81: in function 'update'
            """;

        var result = CrashLogAnalyzer.Analyze(log);

        Assert.Equal(CrashLogKind.LuaError, result.Kind);
        Assert.Equal(@"gamedata\scripts\actor_binder.script", result.File);
        Assert.Equal(81, result.Line);
        Assert.Null(result.KnownIssueId);
    }

    [Fact]
    public void Unknown_text_is_not_promoted_to_a_known_crash_signature()
    {
        var result = CrashLogAnalyzer.Analyze("normal shutdown completed");

        Assert.Equal(CrashLogKind.Unknown, result.Kind);
        Assert.Null(result.KnownIssueId);
        Assert.Contains("no recognized crash marker", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_script_abort_is_summarised_by_the_reason_the_script_logged()
    {
        const string log = """
            ! [LUA][ERROR] ERROR: Insufficient smart_terrain jobs val_smart_terrain_9_6
            FATAL ERROR
            [error]Expression    : fatal error
            [error]Function      : CScriptEngine::lua_error
            [error]Arguments     : LUA error: ...\\gamedata\\scripts\\_g.script:975: bad argument #2 to 'format' (string expected, got nil)
            """;

        var analysis = CrashLogAnalyzer.Analyze(log, "cs");

        Assert.Equal("Insufficient smart_terrain jobs val_smart_terrain_9_6", analysis.Summary);
        Assert.Equal("cs.insufficient-smart-jobs", analysis.KnownIssueId);
    }

    [Fact]
    public void A_script_abort_without_its_reason_line_says_where_the_reason_is()
    {
        var analysis = CrashLogAnalyzer.Analyze(
            "[error]Arguments     : LUA error: ...\\scripts\\_g.script:975: bad argument #2 to 'format' (string expected, got nil)", "cs");

        Assert.Contains("'ERROR:' line", analysis.Summary, StringComparison.Ordinal);
        Assert.Null(analysis.KnownIssueId);
    }
}
