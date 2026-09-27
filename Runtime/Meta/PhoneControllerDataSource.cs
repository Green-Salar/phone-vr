using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace PhoneVr
{
    /// <summary>
    /// Meta Interaction SDK controller source. Point the Controller component's
    /// data source at this instead of From OVR Controller Data Source.
    /// Ray, grab, and the rest of the Interaction SDK then read the phone.
    /// </summary>
    public sealed class PhoneControllerDataSource : DataSource<ControllerDataAsset>
    {
        [SerializeField] PhoneHand hand = PhoneHand.Right;
        [SerializeField] PhoneVrHost host;

        readonly ControllerDataAsset _asset = new ControllerDataAsset();

        public PhoneHand Hand => hand;

        protected override ControllerDataAsset DataAsset => _asset;

        protected override void Start()
        {
            this.BeginStart(ref _started, base.Start);
            if (_asset.Config == null)
                _asset.Config = new ControllerDataSourceConfig();
            _asset.Config.Handedness = hand == PhoneHand.Left ? Handedness.Left : Handedness.Right;
            this.EndStart(ref _started);
        }

        protected override void UpdateData()
        {
            if (host == null)
                host = PhoneVrHost.Instance;

            bool live = host != null && host.TryGetPose(hand, out Pose pose) && host.TryGetSample(out PhoneSample sample);
            _asset.IsDataValid = live;
            _asset.IsConnected = live;
            _asset.IsTracked = live;
            _asset.IsDominantHand = hand == PhoneHand.Right;
            _asset.RootPoseOrigin = PoseOrigin.SyntheticPose;
            _asset.PointerPoseOrigin = PoseOrigin.SyntheticPose;
            if (_asset.Config == null)
                _asset.Config = new ControllerDataSourceConfig();
            _asset.Config.Handedness = hand == PhoneHand.Left ? Handedness.Left : Handedness.Right;

            var input = _asset.Input;
            input.Clear();
            if (live)
            {
                _asset.RootPose = pose;
                _asset.PointerPose = pose;
                bool trigger = sample.Trigger > 0.5f;
                bool grip = sample.Grip > 0.5f;
                input.SetAxis1D(ControllerAxis1DUsage.Trigger, Mathf.Clamp01(sample.Trigger));
                input.SetAxis1D(ControllerAxis1DUsage.Grip, Mathf.Clamp01(sample.Grip));
                input.SetButton(ControllerButtonUsage.TriggerButton, trigger);
                input.SetButton(ControllerButtonUsage.GripButton, grip);
                input.SetButton(ControllerButtonUsage.PrimaryButton, sample.Primary);
                input.SetButton(ControllerButtonUsage.PrimaryTouch, sample.Primary);
                input.SetButton(ControllerButtonUsage.SecondaryButton, sample.Secondary);
                input.SetButton(ControllerButtonUsage.SecondaryTouch, sample.Secondary);
                input.SetButton(ControllerButtonUsage.MenuButton, sample.Menu);
                input.SetButton(ControllerButtonUsage.Primary2DAxisClick, sample.StickClick);
                input.SetButton(ControllerButtonUsage.Primary2DAxisTouch, sample.StickClick || sample.StickX != 0f || sample.StickY != 0f);
                input.SetAxis2D(ControllerAxis2DUsage.Primary2DAxis, new Vector2(sample.StickX, sample.StickY));
            }
            _asset.Input = input;
        }
    }
}
