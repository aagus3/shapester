using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One player line inside a message.</summary>
[Serializable]
public class Row
{
    public long cid;
    public string name;
    public int score;    // total points in the match
    public int points;   // points won this round
    public float area;   // area this round (-1 = no result)
    public int rank;     // place this round (1 = best)
}

/// <summary>Every network message is one of these, as JSON. "t" says which kind.</summary>
[Serializable]
public class Msg
{
    public string t;          // hello | area | roster | round | live | result
    public string name;
    public string state;      // roster: "lobby" or "playing"
    public int round, total, seed, msLeft, msToNext;
    public float area;
    public bool final;
    public long hostCid;
    public Row[] players;
}

/// <summary>
/// Match logic. Runs only on the host (the player who created the room):
/// starts rounds (same random seed = same map for everyone), runs the 15 s timer,
/// collects everyone's area, ranks them and adds points.
/// Points per round = how many players you beat (smaller area wins).
/// </summary>
public class MatchHost : IDisposable
{
    public const int Rounds = 5;
    public const float RoundSeconds = 15f;
    const float Grace = 0.8f;          // wait a bit for the last area reports
    const float ResultSeconds = 4f;    // results screen between rounds
    const float FinalSeconds = 7f;     // final results screen

    class Player { public long cid; public string name; public int score; public float area = -1; }

    readonly NetSession net;
    readonly List<Player> players = new List<Player>();
    bool running, inRound;
    int round;
    float roundEnd, nextAt, liveAt;

    public int PlayerCount => players.Count;
    public bool Running => running;

    public MatchHost(NetSession net)
    {
        this.net = net;
        net.MessageToHost += OnMessage;
        net.ClientLeft += OnClientLeft;
    }

    public void Dispose()
    {
        net.MessageToHost -= OnMessage;
        net.ClientLeft -= OnClientLeft;
    }

    public void AddSelf(string name)
    {
        Upsert((long)net.LocalClientId, name);
        BroadcastRoster();
    }

    Player Find(long cid) => players.Find(p => p.cid == cid);

    void Upsert(long cid, string name)
    {
        var p = Find(cid);
        if (p == null) players.Add(p = new Player { cid = cid });
        p.name = string.IsNullOrEmpty(name) ? "PLAYER" : name;
    }

    void OnMessage(ulong sender, string json)
    {
        Msg m;
        try { m = JsonUtility.FromJson<Msg>(json); } catch { return; }
        if (m == null) return;
        switch (m.t)
        {
            case "hello":
                Upsert((long)sender, NamePrompt.Clean(m.name));
                BroadcastRoster();
                break;
            case "area":
                var p = Find((long)sender);
                if (p != null && running && m.round == round && m.area > 0) p.area = m.area;
                break;
        }
    }

    void OnClientLeft(ulong cid)
    {
        players.RemoveAll(p => p.cid == (long)cid);
        if (running && players.Count < 2) StopMatch();
        else BroadcastRoster();
    }

    public void StartMatch()
    {
        if (running || players.Count < 2) return;
        running = true;
        round = 0;
        foreach (var p in players) p.score = 0;
        net.SetLocked(true);
        NextRound();
    }

    void NextRound()
    {
        round++;
        inRound = true;
        roundEnd = Time.unscaledTime + RoundSeconds;
        liveAt = 0;
        foreach (var p in players) p.area = -1;
        BroadcastRoster();
        net.Broadcast(JsonUtility.ToJson(new Msg
        {
            t = "round", round = round, total = Rounds,
            seed = UnityEngine.Random.Range(1, int.MaxValue),
            msLeft = Mathf.RoundToInt(RoundSeconds * 1000),
        }));
    }

    public void Update()
    {
        if (!running) return;
        float now = Time.unscaledTime;
        if (inRound)
        {
            if (now >= liveAt) BroadcastLive(now);
            if (now >= roundEnd + Grace) EndRound(now);
        }
        else if (now >= nextAt)
        {
            if (round >= Rounds) StopMatch();
            else NextRound();
        }
    }

    void EndRound(float now)
    {
        inRound = false;
        // smaller area = better; players without a result go last
        var order = new List<Player>(players);
        order.Sort((a, b) => Key(a).CompareTo(Key(b)));
        var rows = new List<Row>();
        foreach (var p in order)
        {
            int beaten = 0, better = 0;
            foreach (var o in players)
            {
                if (o == p) continue;
                if (Key(o) > Key(p)) beaten++;
                if (Key(o) < Key(p)) better++;
            }
            int points = p.area > 0 ? beaten : 0;
            p.score += points;
            rows.Add(new Row { cid = p.cid, name = p.name, area = p.area, points = points, score = p.score, rank = better + 1 });
        }
        bool final = round >= Rounds;
        float wait = final ? FinalSeconds : ResultSeconds;
        nextAt = now + wait;
        net.Broadcast(JsonUtility.ToJson(new Msg
        {
            t = "result", round = round, total = Rounds, final = final,
            msToNext = Mathf.RoundToInt(wait * 1000), players = rows.ToArray(),
        }));
    }

    static float Key(Player p) => p.area > 0 ? p.area : float.MaxValue;

    void StopMatch()
    {
        running = false;
        inRound = false;
        net.SetLocked(false);
        BroadcastRoster();
    }

    void BroadcastRoster()
    {
        var rows = new List<Row>();
        foreach (var p in players) rows.Add(new Row { cid = p.cid, name = p.name, score = p.score, area = p.area });
        net.Broadcast(JsonUtility.ToJson(new Msg
        {
            t = "roster", state = running ? "playing" : "lobby", round = round, total = Rounds,
            hostCid = (long)net.LocalClientId, players = rows.ToArray(),
        }));
    }

    void BroadcastLive(float now)
    {
        liveAt = now + 1f;
        var rows = new List<Row>();
        foreach (var p in players) rows.Add(new Row { cid = p.cid, area = p.area });
        net.Broadcast(JsonUtility.ToJson(new Msg { t = "live", round = round, players = rows.ToArray() }));
    }
}
