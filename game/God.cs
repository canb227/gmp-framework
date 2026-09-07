using Godot;
using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

[GenerateShape]
public partial record struct GodSync
{
    public ulong controllingPeerID;
    public bool isHuman;
    public Vector3 pos;
    public Vector3 rot;
    public Vector3 handLoc;
    public Vector3 handRot;
}


/**
 * 
 */ 
[GlobalClass]
public partial class God : Node3D, GMPObject
{
    public int team;
    public bool isHuman;
    public ulong controllingPeerID;

    public int priority { get; set; }
    public bool pauseable { get; set; }
    public ulong id { get; set; }
    public ulong authority { get; set; }
    public ulong owner { get; set; }
    public int priorityAccumulator { get; set; }
    public byte[] desiredState { get; set; }


    [ExportGroup("God Camera")]
    [Export] public float panSpeed = 60.0f;          // world units/sec while fully zoomed out
    [Export] public float edgePanMargin = 12.0f;     // px from a viewport edge that triggers edge-pan
    [Export] public float rotSpeed = 0.005f;         // radians per pixel of mouse motion while RMB held
    [Export] public float zoomStep = 3.0f;           // view-axis units removed/added per wheel notch
    [Export] public float minZoom = 6.0f;            // closest camera distance along the view axis
    [Export] public float maxZoom = 80.0f;           // farthest camera distance along the view axis
    [Export] public float defaultZoom = 32.0f;
    [Export] public float minPitchDeg = 12.0f;       // shallowest downward tilt
    [Export] public float maxPitchDeg = 88.0f;       // steepest tilt (nearly straight down)
    [Export] public float defaultPitchDeg = 55.0f;
    [Export] public float cursorZoomStrength = 1.0f; // how hard the focus slides toward the cursor when zooming in
    [Export] public float rayLength = 5000.0f;       // terrain-pick ray length
    [Export] public float groundHeight = 0.0f;       // world Y of the focus / pan plane


    private Camera3D cam;
    private Node3D hand;
    private MeshInstance3D bodyMesh;

    private Vector3 focus;          // ground point the god is looking at; the eye orbits this
    private float yaw;              // orbit yaw (radians)
    private float pitch;            // downward tilt of the camera (radians, positive = looking down)
    private float zoomDistance;     // eye distance from the focus along the view axis

    private Vector2 rotInput;       // RMB mouse motion accumulated for this frame
    private float zoomInput;        // wheel notches accumulated for this frame (+in / -out)
    private bool rotating;          // RMB held -> free-look, cursor captured
    private Vector2 lastMousePos;   // last cursor position seen while the cursor was visible
    private Vector2 rotAnchorMouse; // cursor position when the current free-look began
    private bool haveMousePos;      // guards edge-pan until we know where the cursor is

    private Vector3 handLoc;        // world position of the terrain pointer (synced via GodSync.handLoc)
    private bool havePointer;       // whether handLoc holds a valid pick yet

    private bool IsController => controllingPeerID == Lobby.selfPeerID;


    public byte[] GenerateStateUpdate()
    {
        GodSync msg = new GodSync
        {
            controllingPeerID = this.controllingPeerID,
            isHuman = this.isHuman,
            pos = this.Position,
            rot = this.Rotation,
            handLoc = this.handLoc,
            handRot = Vector3.Zero
        };
        return GMPObject.serializer.Serialize(msg);
    }

    public void ApplyStateUpdate(byte[] update)
    {
        GodSync msg = GMPObject.serializer.Deserialize<GodSync>(update);
        controllingPeerID = msg.controllingPeerID;
        isHuman = msg.isHuman;
        Position = msg.pos;
        Rotation = msg.rot;
        handLoc = msg.handLoc;

        // Remote proxies only ever get their pointer position from the network.
        EnsureNodes();
        if (hand != null && !IsController)
        {
            hand.GlobalPosition = handLoc;
        }
    }

    public void AfterInit()
    {
        EnsureNodes();

        // Take the scene-authored spot as the ground focus point; the eye orbits it.
        focus = new Vector3(Position.X, groundHeight, Position.Z);
        yaw = Rotation.Y;
        pitch = Mathf.DegToRad(Mathf.Clamp(defaultPitchDeg, minPitchDeg, maxPitchDeg));
        zoomDistance = Mathf.Clamp(defaultZoom, minZoom, maxZoom);

        // The controller derives its own transform each frame; a remote proxy keeps
        // whatever the last state update placed it at.
        if (IsController)
        {
            ApplyRigTransform();
        }

        SeedMousePos();

        // Only the controlling machine drives the viewport.
        if (cam != null)
        {
            cam.Current = IsController;
        }

        if (bodyMesh != null)
        {
            bodyMesh.Visible = !IsController;
        }
    }

    // ---- Node lifecycle -------------------------------------------------

    private void EnsureNodes()
    {
        cam ??= GetNodeOrNull<Camera3D>("Camera3D");
        hand ??= GetNodeOrNull<Node3D>("hand");
        bodyMesh ??= GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
    }

    private void SeedMousePos()
    {
        if (!haveMousePos && IsInsideTree())
        {
            lastMousePos = GetViewport().GetMousePosition();
            haveMousePos = true;
        }
    }

    // ---- Input --------------------------------------------------------

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsController)
        {
            return;
        }

        if (@event is InputEventMouseButton button && button.Pressed)
        {
            if (button.ButtonIndex == MouseButton.WheelUp)
            {
                zoomInput += 1.0f;
            }
            else if (button.ButtonIndex == MouseButton.WheelDown)
            {
                zoomInput -= 1.0f;
            }
        }

        if (@event is InputEventMouseButton rmb && rmb.ButtonIndex == MouseButton.Right)
        {
            if (rmb.Pressed && !rotating)
            {
                rotating = true;
                rotAnchorMouse = lastMousePos;
                Input.MouseMode = Input.MouseModeEnum.Captured;
            }
            else if (!rmb.Pressed && rotating)
            {
                rotating = false;
                Input.MouseMode = Input.MouseModeEnum.Visible;
                EndFreeLook();
            }
        }

        if (@event is InputEventMouseMotion motion)
        {
            if (rotating)
            {
                rotInput += motion.Relative;
            }
            else
            {
                lastMousePos = motion.Position;
                haveMousePos = true;
            }
        }
    }

    // ---- Physics ----------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        if (!IsController)
        {
            return;
        }

        EnsureNodes();
        SeedMousePos();

        float dt = (float)delta;

        // Safety: never stay mouse-captured without RMB actually held.
        if (rotating && !Input.IsMouseButtonPressed(MouseButton.Right))
        {
            rotating = false;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            EndFreeLook();
        }

        ApplyRotation();
        ApplyZoom();
        ApplyPan(dt);
        ApplyRigTransform();
        UpdatePointer();
    }

    private void ApplyRotation()
    {
        if (rotInput == Vector2.Zero)
        {
            return;
        }

        yaw -= rotInput.X * rotSpeed;
        pitch = Mathf.Clamp(
            pitch + rotInput.Y * rotSpeed,
            Mathf.DegToRad(minPitchDeg),
            Mathf.DegToRad(maxPitchDeg));
        rotInput = Vector2.Zero;
    }

    private void ApplyZoom()
    {
        if (Mathf.IsZeroApprox(zoomInput))
        {
            return;
        }

        float previous = zoomDistance;
        zoomDistance = Mathf.Clamp(zoomDistance - zoomInput * zoomStep, minZoom, maxZoom);
        float pulledIn = previous - zoomDistance; // > 0 when zooming in

        // Keep the world point under the cursor roughly fixed while zooming in.
        if (pulledIn > 0.0f && TryGetCursorPoint(out Vector3 target))
        {
            float t = Mathf.Clamp((pulledIn / previous) * cursorZoomStrength, 0.0f, 1.0f);
            focus = focus.Lerp(new Vector3(target.X, focus.Y, target.Z), t);
        }

        zoomInput = 0.0f;
    }

    private void ApplyPan(float dt)
    {
        Vector2 axis = ReadPanAxis();
        if (axis == Vector2.Zero)
        {
            return;
        }

        if (axis.Length() > 1.0f)
        {
            axis = axis.Normalized();
        }

        Vector3 move = new Basis(Vector3.Up, yaw) * new Vector3(axis.X, 0, axis.Y);
        move.Y = 0;
        if (move != Vector3.Zero)
        {
            move = move.Normalized();
        }

        float zoomScaled = panSpeed * Mathf.Lerp(0.3f, 1.0f, Mathf.InverseLerp(minZoom, maxZoom, zoomDistance));
        focus += move * zoomScaled * dt;
    }

    private Vector2 ReadPanAxis()
    {

        float x = (Input.IsPhysicalKeyPressed(Key.D) ? 1.0f : 0.0f) - (Input.IsPhysicalKeyPressed(Key.A) ? 1.0f : 0.0f);
        float y = (Input.IsPhysicalKeyPressed(Key.S) ? 1.0f : 0.0f) - (Input.IsPhysicalKeyPressed(Key.W) ? 1.0f : 0.0f);

        // Edge pan: only while the window is focused and we're not free-looking.
        if (!rotating && haveMousePos && GetWindow().HasFocus())
        {
            Vector2 size = GetViewport().GetVisibleRect().Size;
            Vector2 m = lastMousePos;
            bool inside = m.X >= 0 && m.Y >= 0 && m.X <= size.X && m.Y <= size.Y;
            if (inside)
            {
                if (m.X <= edgePanMargin) x -= 1.0f;
                else if (m.X >= size.X - edgePanMargin) x += 1.0f;
                if (m.Y <= edgePanMargin) y -= 1.0f;
                else if (m.Y >= size.Y - edgePanMargin) y += 1.0f;
            }
        }

        return new Vector2(x, y);
    }

    private void ApplyRigTransform()
    {

        Vector3 localArm = new Vector3(0, Mathf.Sin(pitch), Mathf.Cos(pitch)) * zoomDistance;
        Vector3 worldArm = new Basis(Vector3.Up, yaw) * localArm;

        Position = focus + worldArm;
        Rotation = new Vector3(0, yaw, 0);

        if (cam != null)
        {
            cam.Position = Vector3.Zero;
            cam.Rotation = new Vector3(-pitch, 0, 0);
        }
    }

    private void EndFreeLook()
    {

        Vector2 resume = rotAnchorMouse;
        if (havePointer && cam != null && !cam.IsPositionBehind(handLoc))
        {
            resume = cam.UnprojectPosition(handLoc);
        }

        lastMousePos = resume;
        Input.WarpMouse(resume);
    }

    private void UpdatePointer()
    {
        if (hand == null)
        {
            return;
        }

        if (rotating)
        {

            if (havePointer)
            {
                hand.GlobalPosition = handLoc;
                if (cam != null && !cam.IsPositionBehind(handLoc))
                {
                    lastMousePos = cam.UnprojectPosition(handLoc);
                }
            }
            return;
        }

        if (TryGetCursorPoint(out Vector3 point))
        {
            handLoc = point;
            havePointer = true;
            hand.GlobalPosition = point;
            hand.GlobalRotation = new Vector3(0, hand.GlobalRotation.Y, 0); // keep the pointer upright
        }
    }

    private bool TryGetCursorPoint(out Vector3 point)
    {
        point = Vector3.Zero;
        if (cam == null)
        {
            return false;
        }

        Vector3 from = cam.ProjectRayOrigin(lastMousePos);
        Vector3 dir = cam.ProjectRayNormal(lastMousePos);

        var query = PhysicsRayQueryParameters3D.Create(from, from + dir * rayLength);
        query.CollisionMask = uint.MaxValue; // every layer; the ground just needs a collider
        var result = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (result.Count > 0)
        {
            point = (Vector3)result["position"];
            return true;
        }

        // Fallback: intersect the horizontal plane at the focus height.
        Plane ground = new Plane(Vector3.Up, groundHeight);
        Vector3? planeHit = ground.IntersectsRay(from, dir);
        if (planeHit.HasValue)
        {
            point = planeHit.Value;
            return true;
        }

        return false;
    }
}
