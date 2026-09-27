# Phone VR Controller

A phone on the same Wi-Fi becomes the controller inside a Meta Interaction SDK scene. The headset stays on Meta tracking. Ray, grab, and the other Interaction SDK features read this the same way they read a Touch controller.

The browser cannot supply room-scale position, so this is rotation, a thumbstick, and buttons. The controller sits at a fixed offset from the headset. Tap Center on the phone while it points forward to set the aim.

## Add it to a Unity project

The project needs the Meta XR Interaction SDK (the Essentials package, assembly `Oculus.Interaction`).

In Package Manager, choose Add package from disk and select this folder's `package.json`.

1. Add a `Phone Vr Host` component anywhere in the scene.
2. On the hand that should be the phone, add `Phone Controller Data Source`. Set Hand to Right or Left.
3. On that object's `Controller` component, set the controller data source to `Phone Controller Data Source` instead of `From OVR Controller Data Source`.
4. Press Play. The Game view lists an `https://` address. Open that on the phone, accept the certificate warning, tap Start, then tap Center while the phone points forward.

Allow the app through Windows Firewall if the phone cannot open the page. Use the Wi-Fi address, not `127.0.0.1`, when the phone is another device.

## Phone controls

- Trigger selects.
- Grip grabs.
- The pad is the thumbstick. Press it for a stick click.
- A and B are the primary and secondary buttons.
- Aim: top of phone treats the phone like a wand. Aim: out the back looks through the phone.

Motion needs a secure page, which is why the server is HTTPS. iOS will ask for motion permission when you tap Start.

Press Play in the Unity Editor on the PC. The phone connects to that PC. The certificate is created with Windows, so this pass is for the Editor or a Windows player, with the headset on Link or Air Link.
