using UnityEngine;

namespace PhoneVr
{
    /// <summary>
    /// Serves the phone page on the local network and keeps the latest controller sample.
    /// Add one of these to the Meta scene. Headset tracking is unchanged.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class PhoneVrHost : MonoBehaviour
    {
        public static PhoneVrHost Instance { get; private set; }

        [SerializeField] int port = 8091;
        [SerializeField] Transform head;
        [SerializeField] bool showUrlOverlay = true;
        [SerializeField] Vector3 rightOffset = new Vector3(0.22f, -0.18f, 0.42f);
        [SerializeField] Vector3 leftOffset = new Vector3(-0.22f, -0.18f, 0.42f);

        PhoneVrServer _server;
        PhoneSample _sample;
        bool _hasSample;
        bool _connected;
        bool _aligned;
        bool _hasAimMode;
        bool _pointWithTop;
        Quaternion _align = Quaternion.identity;
        Quaternion _phone = Quaternion.identity;
        string _status = "Starting...";

        public bool IsPhoneConnected => _connected && _hasSample;
        public string Status => _status;

        void OnEnable()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning("Phone VR already has a host in this scene.");
            Instance = this;
            _server = new PhoneVrServer { Log = message => Debug.Log(message) };
            try
            {
                _server.Start(port);
                _status = "Open this on the phone, accept the certificate, then tap Start.";
            }
            catch (System.Exception ex)
            {
                _status = ex.Message;
                Debug.LogError(ex);
            }
        }

        void OnDisable()
        {
            _server?.Dispose();
            _server = null;
            _connected = false;
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            if (head == null && Camera.main != null)
                head = Camera.main.transform;
            if (_server == null)
                return;

            _connected = _server.ClientConnected;
            if (!_server.TryGetSample(out PhoneSample sample))
            {
                _status = _connected ? "Phone connected, waiting for input." : _status;
                return;
            }

            _sample = sample;
            _hasSample = true;
            if (_hasAimMode && sample.PointWithTop != _pointWithTop)
                _aligned = false;
            _pointWithTop = sample.PointWithTop;
            _hasAimMode = true;
            _phone = ToUnity(PhoneOrientation.ToUnity(sample.Alpha, sample.Beta, sample.Gamma, sample.Screen, sample.PointWithTop));
            if (_server.ConsumeRecenter())
            {
                Quaternion reference = head != null ? head.rotation : Quaternion.identity;
                _align = reference * Quaternion.Inverse(_phone);
                _aligned = true;
            }

            _status = _connected
                ? (_aligned ? "Phone is the controller." : "Tap Center on the phone while it points forward.")
                : "Phone disconnected. Tap Start again.";
        }

        public bool TryGetPose(PhoneHand hand, out Pose pose)
        {
            if (!IsPhoneConnected)
            {
                pose = default;
                return false;
            }

            Quaternion aim = _aligned ? _align * _phone : _phone;
            Vector3 offset = hand == PhoneHand.Left ? leftOffset : rightOffset;
            Vector3 position = head != null ? head.position + head.rotation * offset : offset;
            pose = new Pose(position, aim);
            return true;
        }

        public bool TryGetSample(out PhoneSample sample)
        {
            sample = _sample;
            return IsPhoneConnected;
        }

        void OnGUI()
        {
            if (!showUrlOverlay || _server == null || _server.Urls == null)
                return;
            string body = string.Join("\n", _server.Urls) + "\n" + _status;
            float height = 36f + 22f * Mathf.Max(1, _server.Urls.Length + 2);
            GUI.Box(new Rect(12f, 12f, 520f, height), "Phone VR controller\n" + body);
        }

        static Quaternion ToUnity(System.Numerics.Quaternion q)
        {
            return new Quaternion(q.X, q.Y, q.Z, q.W);
        }
    }

    public enum PhoneHand
    {
        Right = 0,
        Left = 1
    }
}
