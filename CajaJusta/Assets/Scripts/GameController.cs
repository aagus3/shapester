using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Services.Multiplayer;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Shapester: random geometric shapes appear; players drag them (1 finger) or move+rotate
/// them (2 fingers). Shapes have physics: when the dragged one hits another it pivots.
/// A rectangle wraps all the shapes; the smaller, the better.
///
/// Modes:
///  - PRACTICE: solo, new map whenever you want.
///  - ONLINE: rooms of 2-6 players (Unity Multiplayer Sessions + Relay). Every round lasts
///    15 s, everyone gets the same map; smallest area wins the most points.
///
/// Retro look: the scene is rendered at low resolution and scaled up with chunky pixels,
/// plus a pixel font and CRT scanlines. Everything is built from code.
/// </summary>
public class GameController : MonoBehaviour
{
    // --- Config ---
    const float BoardW = 12f, BoardH = 20f;      // fixed so every player gets the same map
    const int PieceCount = 15;
    const float SnapDeg = 4f;
    const float MaxDragSpeed = 80f;
    const float TargetRetroWidth = 230f;
    const int SortBoard = 0, SortGrid = 1, SortBoxFill = 2, SortPiece = 10, SortSelected = 20, SortBoxLine = 30, SortHandle = 40;

    // --- Retro palette ---
    static readonly Color BgColor = Hex(0x1a1c2c);
    static readonly Color BoardColor = Hex(0x23263a);
    static readonly Color GridColor = Hex(0x333c57);
    static readonly Color BoxLineColor = Hex(0xffcd75);
    static readonly Color BoxFillColor = new Color(1f, 0.8f, 0.46f, 0.08f);
    static readonly Color HandleColor = Hex(0xf4f4f4);
    static readonly Color TextColor = Hex(0xf4f4f4);
    static readonly Color DimText = Hex(0x94b0c2);
    static readonly Color AlertColor = Hex(0xff5a5a);
    static readonly Color GoodColor = Hex(0xa7f070);
    static readonly Color[] PiecePalette =
    {
        Hex(0xb13e53), Hex(0xef7d57), Hex(0xa7f070), Hex(0x38b764), Hex(0x257179),
        Hex(0x3b5dc9), Hex(0x41a6f6), Hex(0x73eff7), Hex(0xff77a8), Hex(0xc29fff),
        Hex(0xffa300), Hex(0x8f563b),
    };

    // ------------------------------------------------------------------ screens / online state
    enum View { Menu, Busy, Rooms, Room, Online, Practice }
    enum Phase { Playing, Locked, Results }

    View view = View.Menu;
    string busyText = "";
    string status = "";
    float statusUntil;
    string playerName;

    NetSession net;
    MatchHost host;
    List<ISessionInfo> rooms = new List<ISessionInfo>();
    bool roomsLoading;
    float roomsRefreshAt;

    Msg roster, results;
    readonly Dictionary<long, float> liveAreas = new Dictionary<long, float>();
    Phase phase;
    float localEnd, resultsUntil, areaSendAt;
    int curRound, totalRounds;
    bool inputLocked;

    readonly List<(Rect rect, Action action)> uiButtons = new List<(Rect, Action)>();

    // ------------------------------------------------------------------ board state
    readonly List<Piece> pieces = new List<Piece>();
    float totalArea, bestEfficiency;

    Camera cam;
    Transform world, piecesRoot;
    Material meshMat;
    Sprite solidSprite, circleSprite;
    readonly SpriteRenderer[] boxBars = new SpriteRenderer[4];
    SpriteRenderer boxFill, handleDot, handleLine;
    Rect boxVisual;

    // interaction
    enum Mode { None, Pending, Drag, Rotate, Twist }
    Mode mode;
    Piece selected;
    Vector2 pressPos, pressWorld;
    float grabAngleOffset, rotateTarget, pendingSpin;
    bool spinActive;
    bool ignoreUntilRelease, changedThisGesture;
    Vector2 grabLocal, dragTarget;
    float twistStartAngle, twistPieceAngle, twistTargetAngle;
    Vector2 twistStartMid, twistPiecePos, twistTargetPos;
    int twistId0 = -1, twistId1 = -1;
    readonly ContactPoint2D[] contacts = new ContactPoint2D[8];

    // layout / retro render
    Vector2Int lastScreen;
    Rect safePx;
    int ps = 1;
    RenderTexture lowRes;
    Texture2D scanTex, whiteTex;
    float bigUnit, smallUnit;
    float topBarPx;

    void Awake()
    {
        Application.targetFrameRate = 60;
        Application.runInBackground = true;   // keep the match timer running if the tab loses focus
        Time.fixedDeltaTime = 1f / 60f;
        Physics2D.gravity = Vector2.zero;
#if ENABLE_LEGACY_INPUT_MANAGER
        Input.multiTouchEnabled = true;
#endif
        DontDestroyOnLoad(gameObject);
        cam = Camera.main;
        if (cam == null) cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        DontDestroyOnLoad(cam.gameObject);
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = BgColor;
        cam.transform.rotation = Quaternion.identity;

        var tmp = new GameObject("tmp").AddComponent<SpriteRenderer>();
        meshMat = tmp.sharedMaterial;
        Destroy(tmp.gameObject);

        solidSprite = MakeSolidSprite();
        circleSprite = MakeCircleSprite(16);
        whiteTex = Texture2D.whiteTexture;
        UpdateLayoutIfNeeded();

        world = new GameObject("World").transform;
        DontDestroyOnLoad(world.gameObject);
        var board = MakeQuad(world, solidSprite, BoardColor, SortBoard);
        board.transform.localPosition = new Vector3(BoardW / 2, BoardH / 2, 0);
        board.transform.localScale = new Vector3(BoardW, BoardH, 1);
        BuildGrid();
        BuildWalls();
        BuildBox();
        handleLine = MakeQuad(world, solidSprite, new Color(1, 1, 1, 0.6f), SortHandle);
        handleDot = MakeQuad(world, circleSprite, HandleColor, SortHandle + 1);
        piecesRoot = new GameObject("Pieces").transform;
        piecesRoot.SetParent(world, false);

        playerName = NamePrompt.Clean(PlayerPrefs.GetString("name", "")) ?? NamePrompt.RandomName();

        net = new NetSession();
        net.MessageFromHost += OnHostMessage;
        net.SessionLost += OnSessionLost;

        SetView(View.Menu);
    }

    static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

    void SetView(View v)
    {
        view = v;
        bool board = v == View.Online || v == View.Practice;
        world.gameObject.SetActive(board);
        if (!board) { EndGesture(); Select(null); }
        ignoreUntilRelease = true;   // don't let the tap that changed screens leak into the next one
    }

    void Status(string s) { status = s; statusUntil = Time.unscaledTime + 4f; }

    // ------------------------------------------------------------------ menu actions

    void StartPractice()
    {
        SetView(View.Practice);
        inputLocked = false;
        NewRound(null);
    }

    void EditName()
    {
        NamePrompt.Ask(playerName, n =>
        {
            if (n == null) return;
            playerName = n;
            PlayerPrefs.SetString("name", n);
            PlayerPrefs.Save();
        });
    }

    async void PlayOnline()
    {
        busyText = "CONNECTING";
        SetView(View.Busy);
        try
        {
            await net.InitAsync();
            SetView(View.Rooms);
            RefreshRooms();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SetView(View.Menu);
            Status(e.ToString().Contains("NotLinked") ? "PROJECT NOT LINKED TO UNITY CLOUD" : "ONLINE NOT AVAILABLE");
        }
    }

    async void RefreshRooms()
    {
        if (roomsLoading) return;
        roomsLoading = true;
        try { rooms = await net.ListRoomsAsync(); }
        catch (Exception e) { Debug.LogWarning(e); Status("COULD NOT LOAD ROOMS"); }
        roomsLoading = false;
        roomsRefreshAt = Time.unscaledTime + 5f;
    }

    async void CreateRoom()
    {
        busyText = "CREATING ROOM";
        SetView(View.Busy);
        try
        {
            roster = null;
            await net.CreateRoomAsync(playerName);
            host = new MatchHost(net);
            SetView(View.Room);
            host.AddSelf(playerName);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SetView(View.Rooms);
            Status("COULD NOT CREATE ROOM");
        }
    }

    async void JoinRoom(string id)
    {
        busyText = "JOINING";
        SetView(View.Busy);
        try
        {
            roster = null;
            await net.JoinRoomAsync(id);
            SetView(View.Room);
            net.SendToHost(JsonUtility.ToJson(new Msg { t = "hello", name = playerName }));
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SetView(View.Rooms);
            Status("COULD NOT JOIN");
            RefreshRooms();
        }
    }

    async void LeaveRoom()
    {
        busyText = "LEAVING";
        SetView(View.Busy);
        DisposeHost();
        await net.LeaveAsync();
        SetView(View.Rooms);
        RefreshRooms();
    }

    void DisposeHost()
    {
        host?.Dispose();
        host = null;
    }

    void OnSessionLost(string reason)
    {
        DisposeHost();
        SetView(View.Rooms);
        Status(reason);
        RefreshRooms();
    }

    // ------------------------------------------------------------------ messages from the host

    void OnHostMessage(string json)
    {
        Msg m;
        try { m = JsonUtility.FromJson<Msg>(json); } catch { return; }
        if (m == null) return;
        float now = Time.unscaledTime;
        switch (m.t)
        {
            case "roster":
                roster = m;
                // the match ended (or was cancelled): back to the room, unless the final results are showing
                if (m.state == "lobby" && view == View.Online && !(phase == Phase.Results && results != null && results.final))
                {
                    SetView(View.Room);
                    Status("MATCH ENDED");
                }
                break;

            case "round":
                curRound = m.round;
                totalRounds = m.total;
                results = null;
                liveAreas.Clear();
                SetView(View.Online);
                NewRound(m.seed);
                phase = Phase.Playing;
                inputLocked = false;
                localEnd = now + m.msLeft / 1000f;
                areaSendAt = now + 0.5f;
                break;

            case "live":
                if (m.round != curRound || m.players == null) break;
                foreach (var r in m.players) liveAreas[r.cid] = r.area;
                break;

            case "result":
                results = m;
                LockBoard();
                phase = Phase.Results;
                resultsUntil = now + m.msToNext / 1000f;
                break;
        }
    }

    void SendArea()
    {
        float a = CurrentArea();
        if (a > 0) net.SendToHost(JsonUtility.ToJson(new Msg { t = "area", round = curRound, area = a }));
    }

    void LockBoard()
    {
        EndGesture();
        Select(null);
        inputLocked = true;
    }

    // ------------------------------------------------------------------ board / round

    void BuildGrid()
    {
        var root = new GameObject("Grid").transform;
        root.SetParent(world, false);
        for (int x = 1; x < BoardW; x++)
            for (int y = 1; y < BoardH; y++)
            {
                var d = MakeQuad(root, solidSprite, GridColor, SortGrid);
                d.transform.localPosition = new Vector3(x, y, 0);
                d.transform.localScale = new Vector3(0.12f, 0.12f, 1);
            }
    }

    void BuildWalls()
    {
        var walls = new GameObject("Walls");
        walls.transform.SetParent(world, false);
        const float t = 4f;
        void Wall(float cx, float cy, float w, float h)
        {
            var c = walls.AddComponent<BoxCollider2D>();
            c.offset = new Vector2(cx, cy);
            c.size = new Vector2(w, h);
        }
        Wall(-t / 2, BoardH / 2, t, BoardH + 2 * t);
        Wall(BoardW + t / 2, BoardH / 2, t, BoardH + 2 * t);
        Wall(BoardW / 2, -t / 2, BoardW + 2 * t, t);
        Wall(BoardW / 2, BoardH + t / 2, BoardW + 2 * t, t);
    }

    /// <summary>New map. With a seed, everyone who uses the same seed gets the exact same map.</summary>
    void NewRound(int? seed)
    {
        if (seed.HasValue) Random.InitState(seed.Value);
        Select(null);
        foreach (var p in pieces) p.Destroy();
        pieces.Clear();
        mode = Mode.None;
        totalArea = 0; bestEfficiency = 0;

        int colorOffset = Random.Range(0, PiecePalette.Length);
        for (int i = 0; i < PieceCount; i++)
        {
            var color = PiecePalette[(i + colorOffset) % PiecePalette.Length];
            var piece = new Piece(ShapeGenerator.RandomShape(), color, meshMat, piecesRoot);
            if (!PlaceRandomly(piece)) { piece.Destroy(); continue; }
            pieces.Add(piece);
            totalArea += piece.Area;
            piece.SetSorting(SortPiece);
        }
        if (seed.HasValue) Random.InitState(Environment.TickCount);   // don't leave the global random predictable
        Physics2D.SyncTransforms();
        boxVisual = ComputeBox();
    }

    bool PlaceRandomly(Piece p)
    {
        for (int attempt = 0; attempt < 4000; attempt++)
        {
            float gap = attempt < 2000 ? 0.25f : 0f;
            var pos = new Vector2(Random.Range(p.Radius, BoardW - p.Radius), Random.Range(p.Radius, BoardH - p.Radius));
            float angle = Random.value * 360f;
            p.Pos = pos; p.Angle = angle;
            var w = p.World();
            if (gap > 0)
                for (int i = 0; i < w.Length; i++) w[i] = pos + (w[i] - pos) * (1f + gap / p.Radius);
            bool ok = true;
            foreach (var o in pieces)
            {
                if ((o.Pos - pos).magnitude > o.Radius + p.Radius + gap) continue;
                if (ShapeGenerator.Overlap(w, o.World(), out _, out _)) { ok = false; break; }
            }
            if (ok) { p.Place(pos, angle); return true; }
        }
        return false;
    }

    // ------------------------------------------------------------------ loop

    void Update()
    {
        UpdateLayoutIfNeeded();
        NamePrompt.Update();
        float now = Time.unscaledTime;

        if (HandleUIButtons()) { }   // a button took the tap
        else if (view == View.Practice || (view == View.Online && !inputLocked))
        {
            foreach (var p in pieces) p.Sync();
            HandleInput();
        }

        host?.Update();

        if (view == View.Rooms && !roomsLoading && now >= roomsRefreshAt) RefreshRooms();

        if (view == View.Online)
        {
            if (phase == Phase.Playing)
            {
                if (now >= areaSendAt) { SendArea(); areaSendAt = now + 0.5f; }
                if (now >= localEnd)
                {
                    LockBoard();
                    SendArea();
                    phase = Phase.Locked;
                }
            }
            else if (phase == Phase.Results && results != null && results.final && now >= resultsUntil)
            {
                SetView(View.Room);
            }
        }

        if (view == View.Online || view == View.Practice)
        {
            foreach (var p in pieces) p.Sync();
            float dt = Time.deltaTime;
            foreach (var p in pieces) p.Animate(dt);
            var box = ComputeBox();
            float k = 1f - Mathf.Exp(-18f * dt);
            boxVisual = new Rect(
                Mathf.Lerp(boxVisual.x, box.x, k), Mathf.Lerp(boxVisual.y, box.y, k),
                Mathf.Lerp(boxVisual.width, box.width, k), Mathf.Lerp(boxVisual.height, box.height, k));
            DrawBox(boxVisual);
            DrawHandle();
        }
    }

    /// <summary>Taps on retro buttons (drawn in OnGUI). Returns true if a button was hit.</summary>
    bool HandleUIButtons()
    {
        if (!PointerInput.Get(out var pos, out var down, out _, out _) || !down) return false;
        var gui = new Vector2(pos.x, Screen.height - pos.y);
        Action hit = null;
        foreach (var b in uiButtons)
            if (b.rect.Contains(gui)) { hit = b.action; break; }
        if (hit == null) return false;
        ignoreUntilRelease = true;
        hit();
        return true;
    }

    void FixedUpdate()
    {
        if (selected == null) return;
        var rb = selected.Body;
        float dt = Time.fixedDeltaTime;

        if (mode == Mode.Drag)
        {
            var v = (dragTarget - rb.GetRelativePoint(grabLocal)) / dt;
            rb.linearVelocity = Vector2.ClampMagnitude(v, MaxDragSpeed);
            if (rb.GetContacts(contacts) == 0) rb.angularVelocity = 0;
        }
        else if (mode == Mode.Twist)
        {
            rb.linearVelocity = Vector2.ClampMagnitude((twistTargetPos - rb.position) / dt, MaxDragSpeed);
            rb.angularVelocity = Mathf.Clamp(Mathf.DeltaAngle(rb.rotation, twistTargetAngle) / dt, -1500f, 1500f);
        }

        if (mode == Mode.Rotate)
            rb.angularVelocity = Mathf.Clamp(Mathf.DeltaAngle(rb.rotation, rotateTarget) * 15f, -900f, 900f);

        if (pendingSpin != 0)
        {
            rb.angularVelocity = pendingSpin / dt;
            pendingSpin = 0;
            spinActive = true;
        }
        else if (spinActive)
        {
            rb.angularVelocity = 0;
            spinActive = false;
        }
    }

    void HandleInput()
    {
        if (PointerInput.TwoFingers(out var f0, out var f1, out int id0, out int id1))
        {
            if (ignoreUntilRelease) return;
            if (mode == Mode.Twist && id0 == twistId1 && id1 == twistId0)
            {
                (f0, f1) = (f1, f0);
                (id0, id1) = (id1, id0);
            }
            bool sameFingers = id0 == twistId0 && id1 == twistId1;
            var w0 = ScreenToWorld(f0); var w1 = ScreenToWorld(f1);
            var mid = (w0 + w1) * 0.5f;
            float a = AngleTo(w0, w1);
            if (mode != Mode.Twist || !sameFingers)
            {
                twistId0 = id0; twistId1 = id1;
                if (mode != Mode.Twist && mode != Mode.Drag && mode != Mode.Pending && mode != Mode.Rotate)
                {
                    var hit = PieceAt(mid) ?? PieceAt(w0) ?? PieceAt(w1);
                    if (hit != null) Select(hit);
                }
                mode = Mode.Twist;
                twistStartAngle = a;
                twistStartMid = mid;
                if (selected != null)
                {
                    twistPieceAngle = twistTargetAngle = selected.Body.rotation;
                    twistPiecePos = twistTargetPos = selected.Body.position;
                }
            }
            else if (selected != null)
            {
                float d = Mathf.DeltaAngle(twistStartAngle, a);
                twistTargetAngle = twistPieceAngle + d;
                twistTargetPos = twistPiecePos + (mid - twistStartMid);
                changedThisGesture = true;
            }
            return;
        }

        if (!PointerInput.Get(out var pos, out var down, out var held, out var up)) return;
        var w = ScreenToWorld(pos);

        if (mode == Mode.Twist)
        {
            if (held && !up && selected != null)
            {
                mode = Mode.Drag;
                twistId0 = twistId1 = -1;
                Grab(w);
            }
            else EndGesture();
            return;
        }

        float spin = PointerInput.RotateInput();
        if (selected != null && spin != 0 && !ignoreUntilRelease)
        {
            pendingSpin += spin;
            if (mode == Mode.None) UpdateBest(); else changedThisGesture = true;
        }

        if (ignoreUntilRelease)
        {
            if (!held || up) { ignoreUntilRelease = false; EndGesture(); }
            return;
        }

        if (down)
        {
            changedThisGesture = false;
            if (IsOverUI(pos)) { ignoreUntilRelease = true; return; }
            if (selected != null && (w - HandlePos(selected)).magnitude < 0.9f)
            {
                mode = Mode.Rotate;
                grabAngleOffset = selected.Angle - AngleTo(selected.Pos, w);
                rotateTarget = selected.Angle;
            }
            else
            {
                var hit = PieceAt(w);
                Select(hit);
                if (hit != null)
                {
                    mode = Mode.Pending;
                    pressPos = pos;
                    pressWorld = w;
                }
                else mode = Mode.None;
            }
        }

        if (held && selected != null)
        {
            if (mode == Mode.Pending && (pos - pressPos).magnitude > Mathf.Max(10f, Screen.height * 0.01f))
            {
                mode = Mode.Drag;
                Grab(pressWorld);
            }
            if (mode == Mode.Drag)
            {
                dragTarget = w;
                changedThisGesture = true;
            }
            else if (mode == Mode.Rotate)
            {
                rotateTarget = SnapAngle(selected, AngleTo(selected.Pos, w) + grabAngleOffset);
                changedThisGesture = true;
            }
        }

        if (up) EndGesture();
    }

    void Grab(Vector2 world)
    {
        if (selected == null) return;
        grabLocal = selected.Body.GetPoint(world);
        dragTarget = world;
    }

    void EndGesture()
    {
        twistId0 = twistId1 = -1;
        if (selected != null) selected.Stop();
        if (changedThisGesture) UpdateBest();
        changedThisGesture = false;
        mode = Mode.None;
    }

    void Select(Piece p)
    {
        if (selected == p) return;
        pendingSpin = 0;
        if (selected != null) { selected.SetSelected(false); selected.SetSorting(SortPiece); }
        selected = p;
        if (selected != null) { selected.SetSelected(true); selected.SetSorting(SortSelected); }
    }

    Piece PieceAt(Vector2 w)
    {
        if (selected != null && ShapeGenerator.Contains(selected.World(), w)) return selected;
        for (int i = pieces.Count - 1; i >= 0; i--)
            if (ShapeGenerator.Contains(pieces[i].World(), w)) return pieces[i];
        Piece best = null; float bestD = 0.4f;
        foreach (var p in pieces)
        {
            float d = (p.Pos - w).magnitude - p.Radius;
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    float SnapAngle(Piece p, float angle)
    {
        float best = SnapDeg;
        float correction = 0;
        for (int i = 0; i < p.Local.Length; i++)
        {
            var e = p.Local[(i + 1) % p.Local.Length] - p.Local[i];
            float edgeAngle = Mathf.Atan2(e.y, e.x) * Mathf.Rad2Deg + angle;
            float m = Mathf.Repeat(edgeAngle + 45f, 90f) - 45f;
            if (Mathf.Abs(m) < best) { best = Mathf.Abs(m); correction = m; }
        }
        return angle - correction;
    }

    static float AngleTo(Vector2 from, Vector2 to) => Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;

    Vector2 HandlePos(Piece p)
    {
        float a = (p.Angle + 90f) * Mathf.Deg2Rad;
        return p.Pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (p.Radius + 0.8f);
    }

    void DrawHandle()
    {
        bool show = selected != null && mode != Mode.Drag && mode != Mode.Twist;
        handleDot.enabled = handleLine.enabled = show;
        if (!show) return;
        var h = HandlePos(selected);
        handleDot.transform.localPosition = new Vector3(h.x, h.y, 0);
        handleDot.transform.localScale = new Vector3(0.6f, 0.6f, 1);
        var mid = (selected.Pos + h) * 0.5f;
        handleLine.transform.localPosition = new Vector3(mid.x, mid.y, 0);
        handleLine.transform.localRotation = Quaternion.Euler(0, 0, selected.Angle + 90f);
        handleLine.transform.localScale = new Vector3((h - selected.Pos).magnitude, 0.08f, 1);
    }

    // ------------------------------------------------------------------ bounding rectangle

    Rect ComputeBox()
    {
        if (pieces.Count == 0) return new Rect(0, 0, 0, 0);
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        foreach (var p in pieces)
            foreach (var v in p.World()) { min = Vector2.Min(min, v); max = Vector2.Max(max, v); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    float CurrentArea()
    {
        var b = ComputeBox();
        return b.width * b.height;
    }

    void UpdateBest()
    {
        float area = CurrentArea();
        if (area <= 0) return;
        bestEfficiency = Mathf.Max(bestEfficiency, totalArea / area);
    }

    void BuildBox()
    {
        var root = new GameObject("BoundingBox").transform;
        root.SetParent(world, false);
        boxFill = MakeQuad(root, solidSprite, BoxFillColor, SortBoxFill);
        for (int i = 0; i < 4; i++) boxBars[i] = MakeQuad(root, solidSprite, BoxLineColor, SortBoxLine);
    }

    void DrawBox(Rect r)
    {
        const float t = 0.14f;
        boxFill.transform.localPosition = new Vector3(r.center.x, r.center.y, 0);
        boxFill.transform.localScale = new Vector3(r.width, r.height, 1);
        SetBar(boxBars[0], r.center.x, r.yMax, r.width + t, t);
        SetBar(boxBars[1], r.center.x, r.yMin, r.width + t, t);
        SetBar(boxBars[2], r.xMin, r.center.y, t, r.height + t);
        SetBar(boxBars[3], r.xMax, r.center.y, t, r.height + t);
    }

    static void SetBar(SpriteRenderer sr, float x, float y, float w, float h)
    {
        sr.transform.localPosition = new Vector3(x, y, 0);
        sr.transform.localScale = new Vector3(w, h, 1);
    }

    static SpriteRenderer MakeQuad(Transform parent, Sprite s, Color c, int order)
    {
        var go = new GameObject("Quad");
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s; sr.color = c; sr.sortingOrder = order;
        return sr;
    }

    // ------------------------------------------------------------------ layout / camera

    void UpdateLayoutIfNeeded()
    {
        var size = new Vector2Int(Screen.width, Screen.height);
        if (size == lastScreen && safePx == Screen.safeArea) return;
        lastScreen = size;
        safePx = Screen.safeArea;

        ps = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height * 0.6f) / TargetRetroWidth));
        int rtW = Mathf.CeilToInt(Screen.width / (float)ps), rtH = Mathf.CeilToInt(Screen.height / (float)ps);
        if (lowRes != null) { cam.targetTexture = null; lowRes.Release(); Destroy(lowRes); }
        lowRes = new RenderTexture(rtW, rtH, 16) { filterMode = FilterMode.Point };
        lowRes.Create();
        cam.targetTexture = lowRes;
        BuildScanlines();

        float safeWRetro = safePx.width / ps;
        bigUnit = (safeWRetro >= 236 ? 3 : 2) * ps;
        smallUnit = (safeWRetro >= 200 ? 2 : 1) * ps;
        float margin = 4 * ps;
        topBarPx = margin + RetroFont.Height(bigUnit) + 3 * ps + RetroFont.Height(smallUnit) + margin;

        float top = (safePx.yMax - topBarPx) / ps;
        float bottom = (safePx.yMin + margin) / ps;
        float left = safePx.xMin / ps + 2, right = safePx.xMax / ps - 2;
        float availW = Mathf.Max(1, right - left), availH = Mathf.Max(1, top - bottom);

        const float boardMargin = 0.3f;
        float unitsPerPx = Mathf.Max((BoardW + boardMargin * 2) / availW, (BoardH + boardMargin * 2) / availH);
        cam.orthographicSize = rtH * unitsPerPx * 0.5f;

        var boardCenter = new Vector2(BoardW * 0.5f, BoardH * 0.5f);
        var zoneCenter = new Vector2((left + right) * 0.5f, (top + bottom) * 0.5f);
        var offset = (zoneCenter - new Vector2(rtW, rtH) * 0.5f) * unitsPerPx;
        cam.transform.position = new Vector3(boardCenter.x - offset.x, boardCenter.y - offset.y, -10);
    }

    Vector2 ScreenToWorld(Vector2 px)
    {
        var w = cam.ScreenToWorldPoint(new Vector3(px.x / ps, px.y / ps, 10));
        return new Vector2(w.x, w.y);
    }

    bool IsOverUI(Vector2 px) => px.y > safePx.yMax - topBarPx;

    void BuildScanlines()
    {
        if (scanTex != null) Destroy(scanTex);
        int h = Mathf.Max(2, ps);
        scanTex = new Texture2D(1, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
        for (int y = 0; y < h; y++) scanTex.SetPixel(0, y, y == 0 ? new Color(0, 0, 0, 0.22f) : new Color(0, 0, 0, 0));
        scanTex.Apply();
    }

    // ------------------------------------------------------------------ UI

    static readonly CultureInfo CI = CultureInfo.InvariantCulture;
    float SX0 => Snap(safePx.xMin + 4 * ps);
    float SX1 => Snap(safePx.xMax - 4 * ps);
    float SY0 => Snap(Screen.height - safePx.yMax + 4 * ps);
    float SY1 => Snap(Screen.height - safePx.yMin - 4 * ps);

    void OnGUI()
    {
        if (lowRes == null || Event.current.type != EventType.Repaint) return;
        uiButtons.Clear();

        // 1) the low-res scene, scaled up with chunky pixels
        float dispW = lowRes.width * ps, dispH = lowRes.height * ps;
        var screenRect = new Rect(0, Screen.height - dispH, dispW, dispH);
        if (view == View.Online || view == View.Practice)
            GUI.DrawTexture(screenRect, lowRes, ScaleMode.StretchToFill, false);
        else
            Fill(new Rect(0, 0, Screen.width, Screen.height), BgColor);

        // 2) screen content
        switch (view)
        {
            case View.Menu: DrawMenu(); break;
            case View.Busy: DrawBusy(); break;
            case View.Rooms: DrawRooms(); break;
            case View.Room: DrawRoom(); break;
            case View.Practice: DrawPracticeHud(); break;
            case View.Online: DrawOnlineHud(); break;
        }

        // status / error line
        if (!string.IsNullOrEmpty(status) && Time.unscaledTime < statusUntil)
            TextCenter(SY1 - RetroFont.Height(smallUnit), status, FitUnit(status, SX1 - SX0, smallUnit), AlertColor);

        // 3) CRT scanlines over everything
        GUI.DrawTextureWithTexCoords(screenRect, scanTex, new Rect(0, 0, 1, lowRes.height));
    }

    // --- screens ---

    void DrawMenu()
    {
        float w = SX1 - SX0, h = SY1 - SY0;
        float titleUnit = Mathf.Max(bigUnit, Mathf.Floor(w * 0.92f / RetroFont.Width("SHAPESTER", 1) / ps) * ps);
        TextCenter(SY0 + h * 0.15f, "SHAPESTER", titleUnit, BoxLineColor);

        float bh = ButtonH;
        float y = SY0 + h * 0.45f;
        TextCenter(y, "NAME", smallUnit, DimText);
        Button(new Rect(SX0, y + RetroFont.Height(smallUnit) + 3 * ps, w, bh), playerName, EditName);
        y += RetroFont.Height(smallUnit) + bh + 12 * ps;
        Button(new Rect(SX0, y, w, bh), "PLAY ONLINE", PlayOnline);
        y += bh + 5 * ps;
        Button(new Rect(SX0, y, w, bh), "PRACTICE", StartPractice);
    }

    void DrawBusy()
    {
        int dots = (int)(Time.unscaledTime * 3) % 4;
        TextCenter((SY0 + SY1) / 2, busyText + new string('.', dots), FitUnit(busyText + "...", SX1 - SX0, smallUnit), TextColor);
    }

    void DrawRooms()
    {
        float w = SX1 - SX0;
        float y = SY0;
        TextCenter(y, "ROOMS", bigUnit, BoxLineColor);
        y += RetroFont.Height(bigUnit) + 8 * ps;

        float bh = ButtonH;
        float bottomY = SY1 - RetroFont.Height(smallUnit) - 6 * ps - bh * 2 - 5 * ps;   // leave room for buttons + status
        if (rooms.Count == 0)
            TextCenter(y + 10 * ps, roomsLoading ? "LOADING..." : "NO ROOMS YET", smallUnit, DimText);
        foreach (var r in rooms)
        {
            if (y + bh > bottomY) break;
            int count = r.MaxPlayers - r.AvailableSlots;
            string label = (r.Name ?? "ROOM") + "  " + count + "/" + r.MaxPlayers;
            string id = r.Id;
            Button(new Rect(SX0, y, w, bh), label, () => JoinRoom(id));
            y += bh + 4 * ps;
        }

        float by = bottomY + 5 * ps;
        Button(new Rect(SX0, by, w, bh), "CREATE ROOM", CreateRoom);
        by += bh + 5 * ps;
        float half = (w - 5 * ps) / 2;
        Button(new Rect(SX0, by, half, bh), "REFRESH", RefreshRooms);
        Button(new Rect(SX0 + half + 5 * ps, by, half, bh), "BACK", async () =>
        {
            SetView(View.Menu);
            if (net.InSession) await net.LeaveAsync();
        });
    }

    void DrawRoom()
    {
        float w = SX1 - SX0;
        float y = SY0;
        TextCenter(y, "ROOM", bigUnit, BoxLineColor);
        y += RetroFont.Height(bigUnit) + 8 * ps;

        if (roster == null || roster.players == null)
        {
            TextCenter(y + 10 * ps, "WAITING FOR HOST...", FitUnit("WAITING FOR HOST...", w, smallUnit), DimText);
        }
        else
        {
            int n = roster.players.Length;
            TextCenter(y, n + "/" + NetSession.MaxPlayers + " PLAYERS", smallUnit, DimText);
            y += RetroFont.Height(smallUnit) + 8 * ps;
            long me = (long)net.LocalClientId;
            foreach (var p in roster.players)
            {
                string label = p.name + (p.cid == roster.hostCid ? " *" : "");
                var col = p.cid == me ? BoxLineColor : TextColor;
                RetroFont.Draw(SX0 + 4 * ps, y, label, smallUnit, col);
                if (p.score > 0)
                {
                    string s = p.score + " PTS";
                    RetroFont.Draw(SX1 - RetroFont.Width(s, smallUnit) - 4 * ps, y, s, smallUnit, DimText);
                }
                y += RetroFont.Height(smallUnit) + 6 * ps;
            }
            y += 4 * ps;
            RetroFont.Draw(SX0 + 4 * ps, y, "* HOST", ps, DimText);
        }

        float bh = ButtonH;
        float by = SY1 - RetroFont.Height(smallUnit) - 6 * ps - bh * 2 - 5 * ps;
        if (host != null)
        {
            bool canStart = host.PlayerCount >= 2;
            Button(new Rect(SX0, by, w, bh), canStart ? "START" : "NEED 2+ PLAYERS", host.StartMatch, canStart);
        }
        else
        {
            TextCenter(by + (bh - RetroFont.Height(smallUnit)) / 2, "HOST STARTS THE MATCH", FitUnit("HOST STARTS THE MATCH", w, smallUnit), DimText);
        }
        Button(new Rect(SX0, by + bh + 5 * ps, w, bh), "LEAVE", LeaveRoom);
    }

    void DrawPracticeHud()
    {
        float area = CurrentArea();
        float eff = area > 0 ? totalArea / area : 0;
        DrawAreaRow(area);
        float y1 = SY0 + RetroFont.Height(bigUnit) + 3 * ps;
        string stats = "FILL " + (eff * 100f).ToString("0", CI) + "%  BEST " + (bestEfficiency * 100f).ToString("0", CI) + "%";
        float menuW = RetroFont.Width("MENU", ps) + 14 * ps;   // leave room for the MENU button
        RetroFont.Draw(SX0, y1, stats, FitUnit(stats, SX1 - SX0 - menuW, smallUnit), TextColor);

        SmallButton(SX1, SY0, "NEW", () => NewRound(null));
        SmallButton(SX1, y1, "MENU", () => SetView(View.Menu), ps);
        Separator();
    }

    void DrawOnlineHud()
    {
        float area = CurrentArea();
        DrawAreaRow(area);

        // timer (top right)
        float left = Mathf.Max(0, localEnd - Time.unscaledTime);
        string t = phase == Phase.Playing ? Mathf.CeilToInt(left).ToString(CI) : "0";
        var tc = phase == Phase.Playing && left <= 5 ? AlertColor : TextColor;
        RetroFont.Draw(SX1 - RetroFont.Width(t, bigUnit), SY0, t, bigUnit, tc);

        // round + live rank
        float y1 = SY0 + RetroFont.Height(bigUnit) + 3 * ps;
        int players = roster?.players?.Length ?? 1;
        int rank = 1;
        long me = (long)net.LocalClientId;
        foreach (var kv in liveAreas)
            if (kv.Key != me && kv.Value > 0 && kv.Value < area) rank++;
        string info = "ROUND " + curRound + "/" + totalRounds + "  #" + rank + "/" + players;
        RetroFont.Draw(SX0, y1, info, FitUnit(info, (SX1 - SX0) * 0.75f, smallUnit), TextColor);
        SmallButton(SX1, y1, "QUIT", LeaveRoom, ps);
        Separator();

        if (phase == Phase.Locked)
            TextCenter(Snap(Screen.height * 0.5f), "TIME!", bigUnit * 2, BoxLineColor);
        if (phase == Phase.Results && results != null)
            DrawResults();
    }

    void DrawResults()
    {
        float top = Snap(Screen.height - safePx.yMax + topBarPx);
        Fill(new Rect(0, top, Screen.width, Screen.height - top), new Color(BgColor.r, BgColor.g, BgColor.b, 0.9f));

        float w = SX1 - SX0;
        float y = top + 8 * ps;
        string title = results.final ? "FINAL RESULTS" : "ROUND " + results.round + "/" + results.total;
        TextCenter(y, title, FitUnit(title, w, bigUnit), BoxLineColor);
        y += RetroFont.Height(bigUnit) + 8 * ps;

        // table: rank, name, area, +points, total   (26 characters wide)
        float tu = FitUnit("12345678901234567890123456", w, smallUnit);
        float ch = 6 * tu;
        void Col(int col, string s, Color c) => RetroFont.Draw(SX0 + col * ch, y, s, tu, c);
        void ColR(int endCol, string s, Color c) => RetroFont.Draw(SX0 + endCol * ch - RetroFont.Width(s, tu), y, s, tu, c);

        Col(0, "#", DimText); Col(2, "NAME", DimText); ColR(18, "AREA", DimText); ColR(22, "+", DimText); ColR(26, "PTS", DimText);
        y += RetroFont.Height(tu) + 5 * ps;

        var rows = new List<Row>(results.players ?? new Row[0]);
        if (results.final) rows.Sort((a, b) => b.score.CompareTo(a.score));
        long me = (long)net.LocalClientId;
        int place = 0;
        foreach (var r in rows)
        {
            place++;
            var c = r.cid == me ? BoxLineColor : TextColor;
            Col(0, (results.final ? place : r.rank).ToString(CI), c);
            Col(2, r.name, c);
            ColR(18, r.area > 0 ? r.area.ToString("0.0", CI) : "-", c);
            ColR(22, "+" + r.points, r.points > 0 ? GoodColor : DimText);
            ColR(26, r.score.ToString(CI), c);
            y += RetroFont.Height(tu) + 5 * ps;
        }

        if (results.final && rows.Count > 0)
        {
            y += 6 * ps;
            string win = "WINNER: " + rows[0].name;
            TextCenter(y, win, FitUnit(win, w, smallUnit), GoodColor);
            y += RetroFont.Height(smallUnit) + 6 * ps;
        }

        int secs = Mathf.CeilToInt(Mathf.Max(0, resultsUntil - Time.unscaledTime));
        string next = (results.final ? "BACK TO ROOM IN " : "NEXT MAP IN ") + secs;
        TextCenter(y + 8 * ps, next, FitUnit(next, w, smallUnit), DimText);
    }

    void DrawAreaRow(float area)
    {
        RetroFont.Draw(SX0, SY0, "AREA", bigUnit, DimText);
        RetroFont.Draw(SX0 + RetroFont.Width("AREA ", bigUnit), SY0, area.ToString("0.0", CI), bigUnit, BoxLineColor);
    }

    void Separator()
    {
        Fill(new Rect(SX0, Snap(Screen.height - safePx.yMax + topBarPx - 2 * ps), SX1 - SX0, ps), GridColor);
    }

    // --- widgets ---

    float ButtonH => RetroFont.Height(bigUnit) + 8 * ps;

    void Button(Rect r, string label, Action onClick, bool enabled = true)
    {
        r = new Rect(Snap(r.x), Snap(r.y), Snap(r.width), Snap(r.height));
        var col = enabled ? TextColor : DimText;
        DrawFrame(r, ps, col);
        float unit = FitUnit(label, r.width - 8 * ps, smallUnit);
        RetroFont.Draw(Snap(r.center.x - RetroFont.Width(label, unit) / 2), Snap(r.center.y - RetroFont.Height(unit) / 2), label, unit, col);
        if (enabled) uiButtons.Add((r, onClick));
    }

    /// <summary>Small framed button aligned to the right edge "xRight".</summary>
    void SmallButton(float xRight, float y, string label, Action onClick, float unit = -1)
    {
        if (unit < 0) unit = smallUnit;
        float bw = RetroFont.Width(label, unit) + 6 * ps;
        float bh = RetroFont.Height(unit) + 4 * ps;
        var r = new Rect(Snap(xRight - bw), Snap(y - 2 * ps), bw, bh);
        DrawFrame(r, ps, TextColor);
        RetroFont.Draw(Snap(r.x + 3 * ps), Snap(r.y + 2 * ps), label, unit, TextColor);
        uiButtons.Add((new Rect(r.x - 4 * ps, r.y - 4 * ps, r.width + 8 * ps, r.height + 8 * ps), onClick));
    }

    float FitUnit(string s, float maxW, float preferred)
    {
        for (float u = preferred; u > ps; u -= ps)
            if (RetroFont.Width(s, u) <= maxW) return u;
        return ps;
    }

    void TextCenter(float y, string s, float unit, Color c)
    {
        RetroFont.Draw(Snap((SX0 + SX1) / 2 - RetroFont.Width(s, unit) / 2), Snap(y), s, unit, c);
    }

    float Snap(float v) => Mathf.Round(v / ps) * ps;

    void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, whiteTex);
        GUI.color = old;
    }

    void DrawFrame(Rect r, float t, Color c)
    {
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y, t, r.height), c);
        Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    // ------------------------------------------------------------------ generated sprites

    static Sprite MakeSolidSprite()
    {
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var px = new Color[16];
        for (int i = 0; i < 16; i++) px[i] = Color.white;
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
    }

    static Sprite MakeCircleSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = new Vector2(x + 0.5f - size * 0.5f, y + 0.5f - size * 0.5f).magnitude;
                px[y * size + x] = d <= r ? (d > r - 3 ? Color.white : new Color(1, 1, 1, 0.35f)) : Color.clear;
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}

/// <summary>Boots the game in any scene.</summary>
public static class GameBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (UnityEngine.Object.FindAnyObjectByType<GameController>() == null)
            new GameObject("Game").AddComponent<GameController>();
    }
}
