# About Multiplay QoS
The Multiplay QoS (Quality of Service) package provides a Unity implementation of the Multiplay QoS protocol for communicating with the Multiplay QoS service.  This implementation allows a Unity client to determine their network ping to different regions where a Multiplay fleet is deployed.

# Installing Multiplay QoS
To install this package, follow the instructions in the [Package Manager documentation](https://docs.unity3d.com/Packages/com.unity.package-manager-ui@latest/index.html). 

# Using Multiplay QoS
Multiplay provides a Quality of Service (QoS) service to dynamically determine which of the available regions a client would expect to get the best connection quality for their online session.

The service is composed of two main components:
- Discovery
	- Discovery allows the client to determine, at runtime, which regions are currently active to test for connection quality
- QoS
	- QoS allows the client to test for connection quality to each of the available regions

This package assumes familiarity with terms and concepts outlined by Multiplay, such as fleets, locations, and regions.  See https://docs.multiplay.com/ for more information.

# Technical details
## Requirements
This version of Multiplay QoS is compatible with the following versions of the Unity Editor:

* 2019.1 and later (recommended)

## Known limitations
Multiplay QoS version 0.1.0-preview.1 includes the following known limitations:
* Will use 1 job thread for the duration of a set of QoS pings

## Document revision history
|Date|Reason|
|---|---|
|November 6, 2019|Document created|
