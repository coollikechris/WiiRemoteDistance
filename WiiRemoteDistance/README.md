# Wii Remote Distance

A small Windows Forms app that uses [WiimoteLib](https://www.nuget.org/packages/WiimoteLib) to read the infrared camera in a paired Wii Remote and estimate its distance from a Dolphin Bar.

## Start

1. Pair the Wii Remote with the Dolphin Bar using the bar's pairing instructions.
2. Double-click `WiiRemoteDistance.exe`.
3. Select **Connect remote** and point it at both sensor-bar lights.

The estimate uses the apparent horizontal separation of the two lights, a nominal 33-degree Wii Remote camera field of view, and the sensor bar's measured LED spacing (200 mm by default). Adjust LED spacing if your bar differs. The result is approximate and assumes the remote faces the bar; the IR view shows the detected light positions.

The Dolphin Bar can expose multiple Wii Remote HID interfaces. The app tries each available interface instead of assuming the first one is the remote. Its status line shows live battery and accelerometer data; distance appears once both sensor-bar IR lights are visible. If the remote connects but no lights appear, point it toward the bar and check that the bar's sensor LEDs are on.

The app targets .NET Framework 4.8, which is included with many Windows installations. For source builds, install the .NET SDK and run `dotnet build WiiRemoteDistance.csproj`.

## Features

- Calibrate the distance estimate by entering a measured distance while both bar lights are visible.
- Follow the live aiming trail and use the center target and guide to line up the remote.
- Record timestamped distance, infrared position, button, battery, accelerometer, and gesture data to CSV.
- Enable an audible and visual alert below your chosen distance.
- Detect quick accelerometer flicks on the X, Y, and Z axes.
