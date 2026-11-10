# BSR (Beat Saber Ranked)
> [!WARNING]  
> Before you continue: Unfortunately this mod is vibe-coded.
> 
> With that said, this mod went through thorough testing and had it's code reviewed by a human being. If you are ok with that, carry on.

# Requirements
- BSIPA (4.3.0+)
- BeatSaberMarkupLanguage (1.11.0+)
- BS Utils (1.14.0+)
- SongCore (3.12.0+)
- SiraUtil (3.1.0+)
> This mod was tested on 1.40.8 , 1.39.1 and 1.39.0 . Another versions of the game weren't tested
# What this mod brings to the game
- This mod brings the experience of Ranked mode from osu! to Beat Saber, which means:
	- Players are playing duels for ELO points (everyone starts at 1000 ELO)
	- There is an implemented leaderboard system, where players can see each other's elo points and their rank globally
	- Map Draft system that takes maps from ScoreSaber Ranked
  
![UI Showcase](https://i.ibb.co/BHLY1SGq/Screenshot-20261110-195314.png)
# How does this work? 
- In the match players have 100'000 HP, and after the map it calculates the damage dealt by another player.
- Multipliers:
	- Rounds 1 and 2: 1x damage multiplier
	- Rounds 3 and 4: 2x damage multiplier
	- Round 5+: 3x damage multiplier
	- (subject to change with player feedback)
- After the match ends, players gain or lose elo depending on if they won their match or not.
# Gameplay Flow 
-  Player press the "Find Match" button, and after waiting a moment get into a match with another player.
-  The lower elo player gets to pick the map first with a timer of 90 seconds. (If the player doesn't pick the map in the designated time, it's picked randomly)
	- The maps are being picked in the range of 0.5 star from the base star rate, which is calculated by averaging the two players average top page star rate (On ScoreSaber)
-  Players play the map that was picked.
	- While in the map, you can pause for only 3 seconds (this is done in case a player accidentally presses the pause button), after which the Continue button disappears, with an auto-forfeit after 7 seconds in the pause menu.
	- If you press Menu in the pause menu or exit the map by any means, this will forfeit you and you will lose elo if that happens. 
-  Players alternate between picking the maps
-  When one of the players gets to 0 HP, the match ends with the one surviving gaining elo, and the losing player (obviously) losing elo.
	- If for any reasons you want to forfeit between the maps, there is a button on the right to do so.
# Feedback and some more things. 
- If you have any feedback regarding the systems or the mod in general, you can contact me on Discord: patsanmck
- A lot of things are still subject to change
- Server-side is kept private because I am not sure if it's safe to do so. You can view everything the game sends to the server if you are suspicious of somethings







