using System.Net.Http;
using System.Net.WebSockets;
using System.Numerics;
using System.Text;
using PhoneVr;

static Vector3 Forward(Quaternion q)
{
    var v = Vector3.Transform(Vector3.UnitZ, q);
    return v;
}

static void Expect(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
}

Quaternion down = PhoneOrientation.LookRotation(new Vector3(0f, -1f, 0f), new Vector3(0f, 0f, -1f));
Console.WriteLine($"look down quat {down} forward {Forward(down)}");
Quaternion identity = PhoneOrientation.LookRotation(Vector3.UnitZ, Vector3.UnitY);
Console.WriteLine($"look ident quat {identity} forward {Forward(identity)}");
Quaternion flatBack = PhoneOrientation.ToUnity(0, 0, 0, 0, pointWithTop: false);
Quaternion flatTop = PhoneOrientation.ToUnity(0, 0, 0, 0, pointWithTop: true);
Quaternion uprightBack = PhoneOrientation.ToUnity(0, 90, 0, 0, pointWithTop: false);
Quaternion uprightTop = PhoneOrientation.ToUnity(0, 90, 0, 0, pointWithTop: true);
Console.WriteLine($"flat back  {Forward(flatBack)} {flatBack}");
Console.WriteLine($"flat top   {Forward(flatTop)} {flatTop}");
Console.WriteLine($"up back    {Forward(uprightBack)} {uprightBack}");
Console.WriteLine($"up top     {Forward(uprightTop)} {uprightTop}");

Expect(Forward(flatBack).Y < -0.8f, "flat phone, aim out the back, should look down");
Expect(MathF.Abs(Forward(flatTop).Y) < 0.25f, "flat phone, aim with the top, should be horizontal");
Expect(MathF.Abs(Forward(uprightBack).Y) < 0.35f, "upright phone, aim out the back, should be horizontal");
Expect(Forward(uprightTop).Y > 0.8f, "upright phone, aim with the top, should look up");

Vector3 idForward = Forward(identity);
Expect(idForward.Z > 0.99f && MathF.Abs(idForward.X) < 0.01f && MathF.Abs(idForward.Y) < 0.01f, "LookRotation identity failed: " + idForward);

var server = new PhoneVrServer();
server.Log = Console.WriteLine;
server.Start(0);
try
{
    using var handler = new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
    };
    using var http = new HttpClient(handler);
    string page = await http.GetStringAsync(server.Urls[0]);
    Expect(page.Contains("Phone VR", StringComparison.Ordinal), "page did not contain the title");
    Expect(page.Contains("Trigger", StringComparison.Ordinal), "page did not contain the trigger");

    using var socket = new ClientWebSocket();
    socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
    await socket.ConnectAsync(new Uri("wss://127.0.0.1:" + server.Port + "/ws"), CancellationToken.None);
    byte[] payload = Encoding.UTF8.GetBytes("10 20 30 0 1 0 1 0 0 0.5 -0.25 1 1 1");
    await socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);

    PhoneSample sample = default;
    bool got = false;
    for (int i = 0; i < 50 && !got; i++)
    {
        got = server.TryGetSample(out sample);
        if (!got)
            await Task.Delay(20);
    }
    Expect(got, "server did not receive the controller sample");
    Expect(server.ClientConnected, "client was not marked connected");
    Expect(MathF.Abs(sample.Alpha - 10f) < 0.01f, "alpha");
    Expect(MathF.Abs(sample.Beta - 20f) < 0.01f, "beta");
    Expect(sample.Trigger > 0.5f && sample.Primary && sample.StickClick && sample.Recenter && sample.PointWithTop, "buttons");
    Expect(MathF.Abs(sample.StickX - 0.5f) < 0.01f && MathF.Abs(sample.StickY + 0.25f) < 0.01f, "stick");
    Expect(server.ConsumeRecenter(), "recenter was not latched");
    Expect(!server.ConsumeRecenter(), "recenter should be consumed once");
    Console.WriteLine("server ok " + server.Urls[0]);
}
finally
{
    server.Dispose();
}
