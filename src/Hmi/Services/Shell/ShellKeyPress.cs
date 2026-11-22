namespace Hmi.Services.Shell;

/// <summary>One keyboard event forwarded by <c>shell.js</c>. All shortcut decisions stay in C#; the module only forwards.</summary>
public sealed record ShellKeyPress(string Key, bool Ctrl, bool Shift, bool Alt, bool Meta);
