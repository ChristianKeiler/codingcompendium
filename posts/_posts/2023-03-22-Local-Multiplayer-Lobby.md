---
title: "Local Multiplayer Lobby"
date: 2023-03-22
---

During the last one and a half years I worked on a local multiplayer game alongside some friends.
The game features fast paced action divided into very short (think 10-30 seconds on average) rounds in which moles dig around the stage and try to push each other off the map by throwing hammers or dashing into each other.

One of the parts I worked on was the Lobby system. As this was a local multiplayer/ couch-versus game we needed a way to assign controllers to different players. Thanks to a previous project I was already familiar with how to set up a very barebones version of a lobby system.

## Assets used
- Odin Inspector
- Rewired
- Unity Atoms

## Player : Controller is a 1:1 relationship

As we used Rewired I had to make sure every controller was assigned to only one player at most. Not doing so would result in players being able to control different characters at the same time. For this I check every single controller available to the system and assigning it to the SystemPlayer object with the "removeFromOthers" flag set to true. This was initially done inside the Lobby class but we later decided that we would like to return directly to the Lobby should a game be completed instead of sending the players to the Main Menu. Doing so would have meant that they'd have to re-register themselves again, so I extracted the responsible code and only use it for the MainMenu now.

The code to achieve this is simple and consists of 2 loops inside the OnEnable method:

```csharp

    private void OnEnable()
    {
        var systemPlayer = ReInput.players.GetSystemPlayer();
        foreach (var joystick in ReInput.controllers.Joysticks)
        {
            systemPlayer.controllers.AddController(joystick, true);
        }
        foreach(var player in ReInput.players.Players)
        {
            player.isPlaying = false;
        }
    }

```

## Registering and Unregistering

Once I made sure every controller is assigned to only the SystemPlayer I could continue with the registering logic inside the Lobby class. In short: The most obvious way of handling this was to make the players press a button. This would register them as active players, assign the controller to the virtual player and spawn them in the scene.

But how does the system know who's who? This is done by assigning players an ID based on who registered themselves. The first player will get the ID 0, the second one the ID 1 and so on. If player 0 now decides to unregister themself the ID given to the next player will not be 2 but the newly freed 0. This ID system went through a number of iterations, from a very primitive list to a hashset to finally an integer used like a bitmask.

```csharp

    private int _registeredPlayers = 0;

    private int GetFirstAvailableID()
    {
        int id = (int)(Mathf.Log(~_registeredPlayers & -(~_registeredPlayers), 2));
        _registeredPlayers |= (1 << (id));
        return id;
    }
    private void ReleaseID(int id)
    {
        _registeredPlayers &= ~(1 << id);
    }

```

## Lobby Zones

Each player can run around the Lobby scene so we needed a way to tell the system "Hey, I'm ready" or to unregister themselves. This is what the Lobby zones were made for. These zones simply check if a player entered their collider and then invoke an event certain observers can listen to. The Lobby has two lists of LobbyZones, one being for setting the player's status to Ready and one to unregister themselves.

## Conclusion

For this project we aimed to create a Lobby system in which players can figure out the controls before actually starting the game. As only one controller should be assigned to each active player we needed to come up with a proper controller assignment logic.

The fully Lobby script is available [here](https://github.com/ChristianKeiler/codingcompendium/blob/main/Scripts/Lobby.cs).
