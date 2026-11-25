namespace RoHelper.Storage;

/// <summary>同一台電腦上另一個分身的組隊資料（讀它的 config.json 與 status.json）</summary>
public sealed record PartyMate(string Profile, string Role, string Name, string Lobby, bool Alive);

/// <summary>
/// 組隊名單：同一台電腦上其他分身各自是隊長還是隊員、角色名、大廳、有沒有在執行。
/// 為什麼看本機分身而不是好友名單：好友名單裡可能有別人（真的朋友），只邀請、只接受「自己開的帳號」才安全。
/// 角色名來自各分身進野外時讀到的 charName（見 FieldInfoReader.ReadNameAsync）。
/// </summary>
public static class PartyRoster
{
    /// <summary>其他分身（不含自己）；讀不到設定的略過</summary>
    public static IReadOnlyList<PartyMate> Others(string selfProfile, DateTime now)
    {
        var list = new List<PartyMate>();
        foreach (var p in AppPaths.AllProfiles())
        {
            if (string.Equals(p, selfProfile, StringComparison.OrdinalIgnoreCase))
                continue;
            var dir = AppPaths.ProfileDirOf(p);
            try
            {
                if (JsonFile.Read(Path.Combine(dir, "config.json")) is not { } cfg)
                    continue;
                var role = (string?)cfg["partyRole"] ?? "off";
                var name = (string?)cfg["charName"] ?? "";
                var lobby = (string?)cfg["partyLobby"] ?? "約頓海姆";
                var alive = InstanceStatus.Load(dir) is { } st && st.IsAlive(now);
                list.Add(new PartyMate(p, role, name, lobby, alive));
            }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidOperationException)
            {
                // 對方剛好在寫檔：這次略過，下次再讀
            }
        }
        return list;
    }

    /// <summary>隊長要邀請的人：同一個大廳、正在執行、知道角色名的隊員</summary>
    public static IReadOnlyList<string> MemberNames(IEnumerable<PartyMate> mates, string lobby)
        => mates.Where(m => m.Role == "member" && m.Alive && m.Name.Length > 0 && m.Lobby == lobby).Select(m => m.Name).ToList();

    /// <summary>隊員可以接受誰的邀請：本機設成隊長的分身（不管有沒有在執行；剛重開時 status 可能還沒更新）</summary>
    public static IReadOnlyList<string> LeaderNames(IEnumerable<PartyMate> mates)
        => mates.Where(m => m.Role == "leader" && m.Name.Length > 0).Select(m => m.Name).ToList();
}
