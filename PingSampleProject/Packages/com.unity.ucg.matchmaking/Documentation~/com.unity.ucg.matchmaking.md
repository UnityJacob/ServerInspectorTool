# About the **Multiplay Matchmaking Client**
Use the Multiplay Matchmaking Client package to quickly integrate your project with Unity's cloud-based Matchmaking service (see https://unity3d.com/connectedgames for more info).

This package provides a reference Unity (C#) implementation of a matchmaking client which calls the Matchmaking service's web APIs, as well as the latest matchmaking data models which can be used to extend the built-in matchmaking client or build your own.


# Installing the **Multiplay Matchmaking Client**
To install this package, follow the instructions in the [Package Manager documentation](https://docs.unity3d.com/Packages/com.unity.package-manager-ui@latest/index.html).

In addition, this package requires the Google Protobuf library and will not compile without a version of Protobuf installed to your Unity project.  A copy of these libraries can be installed from the "Samples" interface in the Package Manager UI.


# Using the **Multiplay Matchmaking Client**
The Multiplay Matchmaking Client package provides a client API with multiple levels of abstraction which you can choose to use (or not) to communicate with the Multiplay matchmaking service.

Client-to-service implementation can be categorized into the following levels:
*   **Low-level**: Directly call the matchmaking service web APIs (Build your own client)
*   **Mid-level**: (This package) Use the provided data models, serialization/deserialization methods, and methods for sending properly formed UnityWebRequests to the service
*   **High-level**: (This package) Use the MatchmakingRequest object, which can manage the matchmaking state machine for you


## Web API quick-start:
See the Matchmaking documentation for more details.


### Data format:
*   The web API uses Protobuf for data transfer between client and services.


### Web call methods:
|Method|URI|Protobuf request class|Protobuf response class|Description|
|---|---|---|---|---|
|`POST`|`<matchmaker URL>/tickets`|`CreateTicketRequest`|`CreateTicketResponse`|Register a request for matchmaking, uploading custom ticket data.  Server response contains a unique `TicketId` assigned to the request.|
|`GET`|`<matchmaker URL>/tickets?id=<ticketId>`|none|`GetTicketResponse`|Get the current state of the ticket (using the request's `<ticketId>`).  This must currently be polled until an Assignment field is included in the response (which indicates matchmaking completion)|
|`DELETE`|`<matchmaker URL>/tickets?id=<ticketId>`|none|`DeleteTicketResponse`|De-register the request for matchmaking (using the request's `<ticketId>`).  This removes the request from matchmaking on the service end.|

The `MatchmakingClient` class contains convenience methods for sending properly formatted UnityWebRequests for the above web calls, including serialization/deserialzation for the protobuf types.

**The normal call pattern is:**
1) `POST` a new matchmaking request and store the returned ticket Id
2) `GET` the status of that ticket Id until the `Assignment` field of the GET response is populated

`DELETE` is exclusively used for cancelling matchmaking, and can be sent any time after a ticket Id has been received.


### Including custom ticket properties

When sending a POST request to create a matchmaking ticket, you have the option of sending a custom ticket data object.  This is data that clients can pass in to the matchmaking system and be consumed by a Match Function.

To contruct custom ticket data for use with `MatchmakingClient`, use the following object and populate the following fields:

**UnityEngine.Ucg.Matchmaking.Protobuf.CreateTicketRequest**
*   `MapField<string, double> attributes`
    *   A map (similar to C# type Dictionary) of named attributes to values
*   `MapField<string, ByteString> properties`
    *   A map of named object types to a byte[] representation of their values
    *   **Note: Keys should be lowercased**

Both attributes and properties can be processed by a match function, but have somewhat different uses.  For more information, see the documentation on Match Functions.

The sample provided with the package contains an example which populates these fields.


## Using the MatchmakingRequest class
The `MatchmakingRequest` class is a simple to use, high-level implementation built on top of `MatchmakingClient` and the protobuf data model, though it wraps the protobuf types for ease of use.

The steps to using `MatchmakingRequest` are:
1) Create a new `(UnityEngine.Ucg.Matchmaking.)CreateTicketRequest` object and populate it with custom attributes and properties
2) Create a new `MatchmakingRequest` object, passing in the `CreateTicketRequest`
3) Call `SendRequest()` on the request
4) Call the request's `Update()` method every second or less, and wait for the matchmaking request to reach a completed state
5) Consume the results (Error or Assignment)

Note that there are multiple ways to wait for the completion of the request:
1) Yield the `SendRequest()` method if using `MatchmakingRequest` from a coroutine. This also handles calling the request's `Update()` method automatically.
2) Wait for the request's `IsDone` property to be true
3) Register a handler for the request's `Completed` event

Consuming the results:
*   If the `Assignment` on the `MatchmakingRequest` does not contain a populated `.Connection` field, a valid match (to connect you to a server) could not be found
*   You can use the `MatchmakingRequest`'s `State` property to determine the completion state of the request
*   If you are using the `Completed` event, the `MatchmakingRequestCompletionArgs` passed to your handler also contains a `State` field as well as `Assignment` and `Error` fields

For further information, consult the provided sample and the code documentation.


# Technical details
## Requirements
This version of the **Multiplay Matchmaking Client** is compatible with the following versions of the Unity Editor:
*   2019.1 and later (recommended)

This version of the **Multiplay Matchmaking Client** requires that the following third-party libraries be installed:
*   Google Protobuf
    *   Last verified against version 3.8.0


## Known limitations
The **Multiplay Matchmaking Client** version **0.1.0-preview.2** includes the following known limitations:
*   Minimal reliability handling (no retry policies implemented)
*   Not compatible with pure DOTS runtime (reliant on certain "classic" Unity features such as UnityWebRequest)


## Package contents
|Location|Description|
|---|---|
|`\Runtime\`|Contains the runtime code|

Samples may be imported via the Package Manager UI by selecting this package and clicking "Import" next to the sample name

## Document revision history
|Date|Reason|
|---|---|
|November 6, 2019|Document updated for Matchmaking Beta|
|August 15, 2019|Document updated for 0.1.0-preview.2|
|April 10, 2019|Document created for package version 0.1.0-preview|
