using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Roadhog.Tests")]

#if ROADHOG_REACTOR
// System.Text.Json binds constructor parameters to properties by name.
[assembly: System.Reflection.Obfuscation(Feature = @"rule regex: (-renaming) (types=.*) (methods=^\.ctor$)")]
#endif
