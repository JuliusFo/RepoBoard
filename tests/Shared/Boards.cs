using RepoBoard.Core.Board;
using RepoBoard.Core.Git;
using RepoBoard.Core.Repos;

namespace RepoBoard.Testing;

/// <summary>A board on the real git CLI whose configuration lives in <paramref name="folder"/> (review 0.4.0, C12).</summary>
internal static class Boards
{
    public static BoardService New(string folder, TimeProvider time) =>
        new(new GitClient(new GitRunner()), new ConfigStore(Path.Combine(folder, "config.json"), time), time);
}
