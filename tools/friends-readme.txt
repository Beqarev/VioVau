VIOLET VAULTS — how to play with friends
=========================================

STARTING THE GAME
  Windows : unzip, open the folder, double-click VioletVaults.exe.
            Windows may say "Windows protected your PC" (the game isn't code-signed yet):
            click "More info" -> "Run anyway".
  macOS   : unzip, then RIGHT-click "Violet Vaults.app" -> Open -> Open (first time only).
            If macOS says it is "damaged", open Terminal and run:
              xattr -cr ~/Downloads/"Violet Vaults.app"     (adjust the path)
  Linux   : unzip, then run ./VioletVaults.x86_64  (chmod +x it if needed).

PLAYING
  Solo        : Play solo.
  Same couch  : on the menu, each extra player presses SPACE (WASD), ENTER (arrows) or A (gamepad).
  Online      : one person clicks "Host a game". Everyone else types the host's IP and clicks "Join a game".
                Same Wi-Fi: use the host's local IP (e.g. 192.168.x.x).
                Over the internet: easiest is Tailscale (free) — everyone installs it, joins the host's
                Tailscale network, and uses the host's Tailscale IP (100.x.x.x).
                Or the host forwards UDP port 7777 on their router and shares their public IP.

CONTROLS
                 Move          Sprint    Interact   Cards     Pause
  Keyboard 1     WASD          Shift     E          1 2 3     Esc
  Keyboard 2     Arrows        .         /          K L ;     Esc
  Gamepad        Stick/D-pad   RB / RT   A          X Y B     Start

THE GOAL
  Loot chests, lift a Relic from the furthest room, and escape through the rune circle in the
  starting room. Every noise raises the shared Threat — and the vault hunts whoever is loudest.
  The first escape starts a 90-second Collapse. Most points banked wins.
