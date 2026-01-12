using SDL2;
using System;
using System.Data;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.ComponentModel.Design;
using System.Reflection.Emit;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using System.Text.Json;


internal class Program
{
    static void Main(string[] args)
    {
        try
        {
            Fish.LoadFishData("fish_data.json");
            Console.WriteLine("All datas loaded succesfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Err loading JSON: {ex.Message}");
        }
        Game.Start();
    }
}

class Game 
{   
    enum GameState {Menu, Playing, GameMenu, GameOver, Victory}
    static bool pierInitialized = false;
    static bool[,] discovered = new bool[MAP_WIDTH, MAP_HEIGHT];
    static GameState currentState = GameState.Menu;
    static uint lastFishUpdate = 0;
    static uint fishUpdateInterval = 120000;
    const int SCREEN_WIDTH = 800;
    const int SCREEN_HEIGHT = 600;
    const int TILE_SIZE = 10;
    const int MAP_WIDTH = SCREEN_WIDTH * 5 / TILE_SIZE;
    const int MAP_HEIGHT = SCREEN_HEIGHT * 5 / TILE_SIZE;
    static float camX, camY = 0f;
    static float pierX, pierY;
    static bool inShop = false;
    static List<ShopItem> shopItems = new();
    static int shopSelection = 0;    
    
    public static void Start()
    {    
        IntPtr window = SDL.SDL_CreateWindow(
            "GAZE INTO ABYSS",
            SDL.SDL_WINDOWPOS_CENTERED,
            SDL.SDL_WINDOWPOS_CENTERED,
            SCREEN_WIDTH,
            SCREEN_HEIGHT,
            SDL.SDL_WindowFlags.SDL_WINDOW_SHOWN
        );

        IntPtr renderer = SDL.SDL_CreateRenderer(
            window,
            -1, 
            SDL.SDL_RendererFlags.SDL_RENDERER_ACCELERATED
        );

        SDL.SDL_Event e;

        bool running = true;

        float[,] map = new float[MAP_WIDTH, MAP_HEIGHT];

        Map miniMap = new Map();

        if (SDL.SDL_Init(SDL.SDL_INIT_VIDEO) != 0)
        {
            return;
        }

        if (SDL_ttf.TTF_Init() != -1)
        {
            Console.WriteLine("Error: SDL_ttf could not initialize.");
        }  
        IntPtr font = SDL_ttf.TTF_OpenFont("font.ttf", 16);

        if (font == IntPtr.Zero)
        {
            Console.WriteLine("Error: font could not be loaded.");
        }

        if (window == IntPtr.Zero)
        {
            Console.WriteLine("Error: window couldn't be opened.");
            SDL.SDL_Quit();
            return;
        }

        if (renderer == IntPtr.Zero)
        {
            Console.WriteLine("Error: renderer coldn't be created.");
            SDL.SDL_DestroyWindow(window);
            SDL.SDL_Quit();
            return;
        }
        
        GenerateMap(map);
        
        float minDepth = 1f;
        int abyssX = MAP_WIDTH / 2, abyssY = MAP_HEIGHT / 2;
        
        for (int x = 0; x < MAP_WIDTH; x++)
        {
            for (int y = 0; y < MAP_HEIGHT; y++)
            {
                if (map[x,y] < minDepth && map[x, y] < 0.3f)
                {
                    minDepth = map[x, y];
                    abyssX = x;
                    abyssY = y;
                }
            }
        }

        float abyssWorldX = abyssX * TILE_SIZE;
        float abyssWorldY = abyssY * TILE_SIZE;

        IntPtr worldTexture = BakeTileTextures(
            renderer, 
            map, 
            MAP_WIDTH, 
            MAP_HEIGHT, 
            TILE_SIZE
        );
        
        FishingGame fishingGame = new FishingGame();
        Fish currentFish = null;

        float[,] fishMap = new float[MAP_WIDTH, MAP_HEIGHT];
        GenerateFishMap(fishMap); 


        IntPtr fogTexture = GeneratePermanentFog(renderer, MAP_WIDTH, MAP_HEIGHT, TILE_SIZE);
        IntPtr dynamicFogTexture = GenerateDynamicFog(renderer, MAP_WIDTH, MAP_HEIGHT, TILE_SIZE);

        Player player = new Player(map, MAP_WIDTH, MAP_HEIGHT, TILE_SIZE, 0.8f);

        IntPtr boatTexture = GenerateBoat(renderer, player);

        List<Cannonball> cannonballs = new();

        List<DamagePopup> popups = new();

        IntPtr pirateTexture = GeneratePirate(renderer);
        IntPtr kingTexture = PirateKing.CreateTexture(renderer);
        PirateKing pirateKing = new PirateKing(0, 0);

        pirateKing.Spawn(map, MAP_WIDTH, MAP_HEIGHT, TILE_SIZE);

        List<Pirate> allPirates = new();
        allPirates.Add(pirateKing);
        List<(float x, float y)> spawnPoints = new();
        int attempts = 0;
        const int max_attempts = 500;
        const int max_pirates = 10;

        Random rand = new Random();

        while (spawnPoints.Count < max_pirates && attempts < max_attempts)
        {
            attempts++;

            int tx = rand.Next(0, MAP_WIDTH);
            int ty = rand.Next(0, MAP_HEIGHT);

            if (map[tx, ty] >= 0.65f) continue;

            float worldX = tx * TILE_SIZE + rand.Next(-TILE_SIZE/2, TILE_SIZE/2 + 1);
            float worldY = ty * TILE_SIZE + rand.Next(-TILE_SIZE/2, TILE_SIZE/2 + 1);

            float distToPlayer = (float)Math.Sqrt(
                Math.Pow(worldX - player.x, 2) + Math.Pow(worldY - player.y, 2)
            );
            if (distToPlayer < 500f) continue;

            bool tooClose = false;
            foreach (var p in spawnPoints)
            {
                float d = (float)Math.Sqrt(
                    Math.Pow(worldX - p.x, 2) + Math.Pow(worldY - p.y, 2)
                );
                if (d < 300f) 
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            spawnPoints.Add((worldX, worldY));
        }

        foreach (var point in spawnPoints)
        {
            Pirate p = new Pirate(point.x, point.y, 100f);
            p.SpawnPoint = point;
            allPirates.Add(p);
        }

        if (!pierInitialized)
        {
            pierX = player.x - 20;
            pierY = player.y - 20;
            pierInitialized = true;
        }

        while (running)
        {   
            while (SDL.SDL_PollEvent(out e) != 0)
            {
                if (e.type == SDL.SDL_EventType.SDL_QUIT) running = false;

                if (currentState == GameState.Menu)
                {
                    if (e.type == SDL.SDL_EventType.SDL_KEYDOWN)
                    {
                        if (e.key.keysym.scancode == SDL.SDL_Scancode.SDL_SCANCODE_RETURN)
                        {
                            currentState = GameState.Playing;
                        }
                    }
                }
                else if (currentState == GameState.Playing)
                {   
                    if (inShop)
                    {
                        if (e.type == SDL.SDL_EventType.SDL_MOUSEMOTION)
                        {
                            int mX = e.motion.x;
                            int mY = e.motion.y;
                            for (int i = 0; i < shopItems.Count; i++)
                            {
                                SDL.SDL_Rect btnRect = new SDL.SDL_Rect { x = 150, y = 160 + i * 30, w = 400, h = 25 };
                                
                                if (mX >= btnRect.x && mX <= btnRect.x + btnRect.w &&
                                    mY >= btnRect.y && mY <= btnRect.y + btnRect.h)
                                {
                                    shopSelection = i; 
                                }
                            }
                        }

                        if (e.type == SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN && e.button.button == SDL.SDL_BUTTON_LEFT)
                        {
                            if (shopItems.Count > 0)
                            {
                                var item = shopItems[shopSelection];
                                if (player.Money >= item.Cost)
                                {
                                    player.Money -= item.Cost;
                                    item.ApplyUpgrade(player);
                                    shopItems = Shop.GetAvailableItems(player);
                                    if (shopSelection >= shopItems.Count)
                                    {
                                        shopSelection = Math.Max(0, shopItems.Count - 1);
                                    }
                                }
                            }
                        }

                        if (e.type == SDL.SDL_EventType.SDL_KEYDOWN)
                        {
                            if (e.key.keysym.scancode == SDL.SDL_Scancode.SDL_SCANCODE_ESCAPE)
                                inShop = false;
                            
                            if (e.key.keysym.scancode == SDL.SDL_Scancode.SDL_SCANCODE_DOWN)
                                shopSelection = (shopSelection + 1) % shopItems.Count;
                            else if (e.key.keysym.scancode == SDL.SDL_Scancode.SDL_SCANCODE_UP)
                                shopSelection = (shopSelection - 1 + shopItems.Count) % shopItems.Count;
                        }
                    }
                    else
                    {               
                        if (e.type == SDL.SDL_EventType.SDL_KEYDOWN && e.key.repeat == 0)
                        {
                            if (e.key.keysym.scancode == SDL.SDL_Scancode.SDL_SCANCODE_F)
                            {
                                float dx = player.x - pierX;
                                float dy = player.y - pierY;
                                float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                                if (distance < 50f)
                                {   
                                    if (!inShop)
                                    {
                                        inShop = true;
                                        shopItems = Shop.GetAvailableItems(player);
                                        shopSelection = 0;
                                    }
                                }         
                            }
                            else
                            {   
                                int pX = (int)(player.x / TILE_SIZE);
                                int pY = (int)(player.y / TILE_SIZE);

                                if (e.key.keysym.scancode == SDL.SDL_Scancode.SDL_SCANCODE_M)
                                {
                                    if (miniMap.isFullScreen)
                                    {
                                        miniMap.Close();
                                    }
                                    else
                                    {
                                        miniMap.Open();
                                    }
                                }

                                if (e.key.keysym.scancode == SDL.SDL_Scancode.SDL_SCANCODE_E)
                                {
                                    if (!player.IsFishing && fishMap[pX, pY] > 0.7f)
                                    {
                                        player.IsFishing = true;
                                        player.FishIsBiting = false;
                                        player.velocityX = 0f;
                                        player.velocityY = 0f;
                                        player.BiteTimestamp = SDL.SDL_GetTicks() + (uint)new Random().Next(2000, 4000);

                                    }
                                    else if (player.FishIsBiting && !fishingGame.IsActive)
                                    {
                                        currentFish = Fish.GenerateFishAt(
                                            map[pX, pY], 
                                            fishMap[pX, pY],
                                            player.Level
                                        );
                                        fishingGame.Start(currentFish);
                                    }
                                }

                                if (e.key.keysym.scancode == SDL.SDL_Scancode.SDL_SCANCODE_ESCAPE && player.IsFishing)
                                {
                                    player.IsFishing = false;
                                    player.FishIsBiting = false;
                                    Console.WriteLine("Ты перехотел рыбачить...");
                                }

                                if (e.key.keysym.scancode == SDL.SDL_Scancode.SDL_SCANCODE_E && fishingGame.IsActive) fishingGame.PlayerClick();
                                if (e.key.keysym.scancode == SDL.SDL_Scancode.SDL_SCANCODE_ESCAPE && fishingGame.IsActive) fishingGame.Loose();
                                
                                if (player.CannonnsIsAvaible)
                                {
                                    if (e.key.keysym.scancode == SDL.SDL_Scancode.SDL_SCANCODE_Z)
                                    {
                                        var shots = player.ShootLeft();
                                        if (shots != null) cannonballs.AddRange(shots);
                                    }
                                    else if (e.key.keysym.scancode == SDL.SDL_Scancode.SDL_SCANCODE_X)
                                    {
                                        var shots = player.ShootRight();
                                        if (shots != null) cannonballs.AddRange(shots);
                                    }                                       
                                }
                                if (e.key.keysym.scancode == SDL.SDL_Scancode.SDL_SCANCODE_LSHIFT)
                                {
                                    player.Dash();
                                }                 
                            } 
                        }
                    }
                }
            }   

            uint ticks = SDL.SDL_GetTicks();

            SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255);
            SDL.SDL_RenderClear(renderer);

            if (currentState == GameState.Menu)
            {
                RenderMenu(renderer, font);
            }
            else if (currentState == GameState.Playing)
            {   
                player.Update(map, MAP_WIDTH, MAP_HEIGHT, TILE_SIZE);
                
                foreach (var pirate in allPirates)
                {
                    pirate.Update(map, MAP_WIDTH, MAP_HEIGHT, TILE_SIZE, player);
                }

                foreach (var pirate in allPirates)
                {   
                    if (!pirate.IsDead && pirate.currentState == PirateState.Attack)
                    {   
                        if (pirate is PirateKing)
                        {
                            var shots = ((PirateKing)pirate).TripleShootAt(player);
                            if (shots != null) cannonballs.AddRange(shots); 
                        } 
                        else
                        {
                            var shots = pirate.TryShootAt(player);
                            if (shots != null) cannonballs.AddRange(shots);
                        }                   
                    }
                }

                for (int i = cannonballs.Count - 1; i >= 0; i--)
                {
                    var ball = cannonballs[i];
                    ball.Update(map, MAP_WIDTH, MAP_HEIGHT, TILE_SIZE);

                    if (ball.IsExpired())
                    {
                        cannonballs.RemoveAt(i);
                        continue;
                    }

                    if (ball.CheckCollisionWith(player))
                    {
                        player.TakeDamage(ball.GetCurrentDmg());
                        popups.Add(new DamagePopup(player.x, player.y, ball.GetCurrentDmg()));
                        cannonballs.RemoveAt(i);
                        continue;
                    }

                    foreach (var pirate in allPirates)
                    {
                        if (ball.CheckCollisionWith(pirate))
                        {
                            pirate.TakeDamage(ball.GetCurrentDmg());
                            popups.Add(new DamagePopup(pirate.x, pirate.y, ball.GetCurrentDmg()));
                            if (pirate is PirateKing king && king.IsDead && !PirateKing.IsDefeated)
                            {
                                PirateKing.IsDefeated = true;
                                currentState = GameState.Menu;
                                Console.WriteLine("You win the Pirate King");
                            }
                            cannonballs.RemoveAt(i);
                            break;
                        }
                    }
                }

                uint now = SDL.SDL_GetTicks();
                for (int i = allPirates.Count - 1; i >= 0; i--)
                {
                    var pirate = allPirates[i];
                    
                    if (pirate.IsDead && !pirate.DeathTime.HasValue)
                    {
                        pirate.DeathTime = now;
                    }
                    
                    if (pirate.ShouldRespawn(now))
                    {
                        pirate.Respawn();
                    }
                }

                if (player.IsDead)
                {
                    player.Respawn(map, MAP_WIDTH, MAP_HEIGHT, TILE_SIZE);
                }

                camX = player.x - (SCREEN_WIDTH / 2);
                camY = player.y - (SCREEN_HEIGHT / 2);

                camX = Math.Clamp(camX, 0, (MAP_WIDTH * TILE_SIZE) - SCREEN_WIDTH);
                camY = Math.Clamp(camY, 0, (MAP_HEIGHT * TILE_SIZE) - SCREEN_HEIGHT);

                float wave = (float)Math.Sin(ticks * 0.0001f);
                byte colorMod = (byte)(215 + (wave * 40));
                SDL.SDL_SetTextureColorMod(worldTexture, colorMod, colorMod, 255);

                SDL.SDL_RenderClear(renderer);

                SDL.SDL_Rect srcRect = new SDL.SDL_Rect
                {
                    x = (int)camX,
                    y = (int)camY,
                    w = SCREEN_WIDTH,
                    h = SCREEN_HEIGHT
                };
                SDL.SDL_Rect destRect = new SDL.SDL_Rect
                {
                    x = 0,
                    y = 0,
                    w = SCREEN_WIDTH,
                    h = SCREEN_HEIGHT
                };
                SDL.SDL_RenderCopy(renderer, worldTexture, ref srcRect, ref destRect);
                
                int startX = (int)(camX / TILE_SIZE);
                int startY = (int)(camY / TILE_SIZE);
                int endX = startX + (SCREEN_WIDTH / TILE_SIZE) + 1;
                int endY = startY + (SCREEN_HEIGHT / TILE_SIZE) + 1;

                if (ticks - lastFishUpdate > fishUpdateInterval)
                {
                    GenerateFishMap(fishMap);
                    lastFishUpdate = ticks;
                    Console.WriteLine("Косяки рыбы мигрировали в новые места...");
                }
                for (int x = startX; x < endX; x++)
                {
                    for (int y = startY; y < endY; y++)
                    {
                        if (x >= 0 && x < MAP_WIDTH && y >= 0 && y < MAP_HEIGHT)
                        {
                            if (fishMap[x, y] > 0.7f && map[x, y] < 0.75f)
                            {   
                                int bubbleShift = (int)(Math.Sin(ticks * 0.005f + (x + y)) * 2);
                                SDL.SDL_SetRenderDrawColor(renderer, 255, 255, 255, 150);
                                SDL.SDL_Rect bubble = new SDL.SDL_Rect {
                                    x = (int)(x * TILE_SIZE - camX + 4 + bubbleShift),
                                    y = (int)(y * TILE_SIZE - camY + 4),
                                    w = 2, h = 2
                                };
                                SDL.SDL_RenderFillRect(renderer, ref bubble);
                            }
                        }
                    }
                }

                foreach (var pirate in allPirates)
                {
                    if (pirate.IsDead) continue;

                    SDL.SDL_Rect pirateRect = new()
                    {
                        x = (int)(pirate.x - camX),
                        y = (int)(pirate.y - camY),
                        w = 16,
                        h = 16
                    };

                    if (pirate is PirateKing)
                    {   
                        SDL.SDL_Rect kingRect = new() { 
                            x = (int)(pirateKing.x - camX - 16), 
                            y = (int)(pirateKing.y - camY - 24), 
                            w = 32, 
                            h = 48 
                        };
                        SDL.SDL_RenderCopyEx(
                            renderer,
                            kingTexture,
                            IntPtr.Zero,
                            ref kingRect,
                            pirate.angle + 90,
                            IntPtr.Zero,
                            SDL.SDL_RendererFlip.SDL_FLIP_NONE
                        );
                    }
                    else
                    {
                        SDL.SDL_RenderCopyEx(
                            renderer,
                            pirateTexture,
                            IntPtr.Zero,
                            ref pirateRect,
                            pirate.angle,
                            IntPtr.Zero,
                            SDL.SDL_RendererFlip.SDL_FLIP_NONE
                        );
                    }
                }               

                foreach (var pirate in allPirates)
                {
                    if (!pirate.IsDead)
                    {
                        DrawHealthBar(renderer, pirate.x - camX, pirate.y - camY - 20, pirate.currentHealth, pirate.maxHealth);                        
                    }
                }

                SDL.SDL_SetRenderTarget(renderer, fogTexture);
                SDL.SDL_SetRenderDrawBlendMode(renderer, SDL.SDL_BlendMode.SDL_BLENDMODE_NONE);
                SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 0);

                FillCircle(renderer, (int)player.x, (int)player.y, 200);

                SDL.SDL_SetRenderTarget(renderer, dynamicFogTexture);
                SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 180);
                SDL.SDL_RenderClear(renderer);

                SDL.SDL_SetRenderDrawBlendMode(renderer, SDL.SDL_BlendMode.SDL_BLENDMODE_NONE);
                SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 0);

                FillCircle(renderer, (int)player.x, (int)player.y, 180);

                SDL.SDL_SetRenderTarget(renderer, IntPtr.Zero);

                SDL.SDL_RenderCopy(renderer, dynamicFogTexture, ref srcRect, ref destRect);

                SDL.SDL_RenderCopy(renderer, fogTexture, ref srcRect, ref destRect);

                int playerX = (int)(player.x / TILE_SIZE);
                int playerY = (int)(player.y / TILE_SIZE);
                int discoveryRadius = 20;
                for (int dx = - discoveryRadius; dx <= discoveryRadius; dx++)
                {
                    for (int dy = - discoveryRadius; dy <= discoveryRadius; dy++)
                    {
                        int nx = playerX + dx;
                        int ny = playerY + dy;
                        if (nx >= 0 && nx < MAP_WIDTH && ny >= 0 && ny < MAP_HEIGHT)
                        {
                            if (dx * dx + dy * dy <= discoveryRadius * discoveryRadius)
                                discovered[nx, ny] = true;
                        }
                    }
                }

                SDL.SDL_Rect playerRect = new SDL.SDL_Rect
                {
                    x = (int)(player.x - camX),
                    y = (int)(player.y - camY),
                    w = 16,
                    h = 16
                };

                SDL.SDL_RenderCopyEx(
                    renderer, 
                    boatTexture, 
                    IntPtr.Zero, 
                    ref playerRect, 
                    player.angle, 
                    IntPtr.Zero, 
                    SDL.SDL_RendererFlip.SDL_FLIP_NONE
                );

                SDL.SDL_SetRenderDrawColor(renderer, 30, 30, 30, 255);
                foreach (var ball in cannonballs)
                {
                    if (!ball.IsSink)
                    {
                        SDL.SDL_Rect r = new()
                        {
                            x = (int)(ball.x - camX),
                            y = (int)(ball.y - camY),
                            w = 6,
                            h = 6
                        };

                        SDL.SDL_RenderFillRect(renderer, ref r);
                    }
                }

                DrawHealthBar(renderer, player.x - camX, player.y - camY - 20, player.currentHealth, player.maxHealth);
 
                for (int i = popups.Count - 1; i >= 0; i--) if (popups[i].IsExpired()) popups.RemoveAt(i);
                
                foreach (var popup in popups) popup.Draw(renderer, font, camX, camY);               

                SDL.SDL_SetRenderDrawColor(renderer, 101, 67, 33, 255);
                SDL.SDL_Rect pierRect = new SDL.SDL_Rect
                {
                    x = (int)(pierX - camX - 10),
                    y = (int)(pierY - camY - 5),
                    w = 30,
                    h = 30
                };
                SDL.SDL_RenderFillRect(renderer, ref pierRect);

                float distToPier = (float)Math.Sqrt(Math.Pow(player.x - pierX, 2) + Math.Pow(player.y - pierY, 2));
                if (distToPier < 50f) {
                    SDL.SDL_Color gold = new SDL.SDL_Color { r = 255, g = 215, b = 0, a = 255};
                    DrawText(renderer, font, "PRESS [F] TO ENTER IN SHOP", (int)(pierX - camX - 100), (int)(pierY - camY - 30), gold);
                }
                
                int pTileX = (int)(player.x / TILE_SIZE);
                int pTileY = (int)(player.y / TILE_SIZE);

                if (pTileX >= 0 && pTileX < MAP_WIDTH && pTileY >= 0 && pTileY < MAP_HEIGHT)
                {
                    if (fishMap[pTileX, pTileY] > 0.7f && !player.IsFishing)
                    {
                        SDL.SDL_Color gold = new SDL.SDL_Color { r = 255, g = 215, b = 0, a = 255};
                        DrawText(renderer, font, "PRESS [E] TO FISH", SCREEN_WIDTH / 2 - 80, SCREEN_HEIGHT - 400, gold);
                    }
                }

                if (player.IsFishing && !player.FishIsBiting)
                {
                    if (ticks >= player.BiteTimestamp)
                    {
                        player.FishIsBiting = true;
                    }
                }

                if (fishingGame.IsActive)
                {
                    fishingGame.Update();

                    if (fishingGame.CheckWin())
                    {
                        int pX = (int)(player.x / TILE_SIZE);
                        int pY = (int)(player.y / TILE_SIZE);

                        fishMap[pX, pY] = 0f;
                        if (player.AddToInventory(currentFish))
                        {
                            Console.WriteLine($"Вы добавили {currentFish.Name} с весом {currentFish.Weight} в инвентарь.");
                        }
                        else
                        {
                            Console.WriteLine("Инвентарь полон! Рыба уплыла...");
                        }
                        fishingGame.IsActive = false;
                        player.IsFishing = false;
                        player.FishIsBiting = false;

                    }
                    else if (fishingGame.CheckLoss())
                    {
                        Console.WriteLine("Сорвалась...");
                        fishingGame.IsActive = false;
                        player.IsFishing = false;
                        player.FishIsBiting = false;
                    }
                    else if (fishingGame.Loss)
                    {
                        Console.WriteLine("Ты сбежал...");
                        fishingGame.IsActive = false;
                        fishingGame.Loss = false;
                        player.IsFishing = false;
                        player.FishIsBiting = false;
                    }
                }

                if (player.FishIsBiting && !fishingGame.IsActive)
                {
                    SDL.SDL_Color gold = new SDL.SDL_Color { r = 255, g = 215, b = 0, a = 255};
                    DrawText(renderer, font, "FISH IS BITING! PRESS [E] TO HOOK!", SCREEN_WIDTH / 2 - 150, SCREEN_HEIGHT - 400, gold);
                }

                if (fishingGame.IsActive)
                {
                    SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 180);
                    SDL.SDL_Rect bgMiniGame = new SDL.SDL_Rect { x = SCREEN_WIDTH / 2 - 110, y = SCREEN_HEIGHT - 140, w = 220, h = 60 };
                    SDL.SDL_RenderFillRect(renderer, ref bgMiniGame);

                    SDL.SDL_Rect tensionBarBg = new SDL.SDL_Rect { x = SCREEN_WIDTH / 2 - 100, y = SCREEN_HEIGHT - 100, w = 200, h = 15 };
                    SDL.SDL_SetRenderDrawColor(renderer, 100, 100, 100, 255);
                    SDL.SDL_RenderFillRect(renderer, ref tensionBarBg);

                    int pointerPos = (int)(fishingGame.Tension * 10); 
                    SDL.SDL_Rect pointer = new SDL.SDL_Rect { x = SCREEN_WIDTH / 2 + pointerPos - 5, y = SCREEN_HEIGHT - 105, w = 10, h = 25 };
                    SDL.SDL_SetRenderDrawColor(renderer, 255, 50, 50, 255);
                    SDL.SDL_RenderFillRect(renderer, ref pointer);

                    float progPercent = Math.Clamp(fishingGame.Progress / fishingGame.MaxProgress, 0f, 1f);
                    SDL.SDL_Rect progBar = new SDL.SDL_Rect { x = SCREEN_WIDTH / 2 - 100, y = SCREEN_HEIGHT - 125, w = (int)(200 * progPercent), h = 10 };
                    SDL.SDL_SetRenderDrawColor(renderer, 50, 255, 50, 255);
                    SDL.SDL_RenderFillRect(renderer, ref progBar);
                }            

                SDL.SDL_SetRenderDrawColor(renderer, 255, 255, 255, 255);
                SDL.SDL_Rect goldIcon = new SDL.SDL_Rect { x = 10, y = 20, w = 10, h = 10 };
                SDL.SDL_SetRenderDrawColor(renderer, 255, 215, 0, 255);
                SDL.SDL_RenderFillRect(renderer, ref goldIcon);

                SDL.SDL_Color white = new SDL.SDL_Color { r = 255, g = 255, b = 255, a = 255 };

                float distToBoss = (float)Math.Sqrt(Math.Pow(player.x - pirateKing.x, 2) + Math.Pow(player.y - pirateKing.y, 2));

                if (distToBoss < 500f) 
                {
                    pirateKing.HasBeenSeen = true;
                }

                DrawText(renderer, font, $"GOLD: {(int)player.Money}", 25, 20, white);
                DrawText(renderer, font, $"LVL: {player.Level}", 25, 40, white);
                DrawText(renderer, font, $"EXP: {player.Experience}/{player.Level * 100}", 100, 40, white);
                DrawText(renderer, font, $"CARGO: {player.Inventory.Count}/{player.MaxInventorySlots}", 25, 60, white);
                DrawText(renderer, font, $"HP: {(int)player.currentHealth}/{player.maxHealth}", 25, 80, white);
                miniMap.Draw(renderer, map, fishMap, discovered, player, pirateKing, miniMap, SCREEN_WIDTH, SCREEN_HEIGHT, MAP_WIDTH, MAP_HEIGHT, TILE_SIZE);
                if (inShop)
                {
                    Shop.DrawShop(renderer, font, shopItems, shopSelection, camX, camY);                   
                }
            }

            SDL.SDL_RenderPresent(renderer);
            SDL.SDL_Delay(16);
        }


        SDL.SDL_DestroyTexture(boatTexture);
        SDL.SDL_DestroyTexture(pirateTexture);
        SDL.SDL_DestroyTexture(fogTexture);
        SDL.SDL_DestroyTexture(dynamicFogTexture);
        SDL.SDL_DestroyTexture(worldTexture);
        SDL.SDL_DestroyRenderer(renderer);
        SDL.SDL_DestroyWindow(window);
        SDL.SDL_Quit();
    }
    
    static void RenderMenu(IntPtr renderer, IntPtr font)
    {
        SDL.SDL_Color titleColor = new SDL.SDL_Color { r = 0, g = 200, b = 255, a = 255 };
        SDL.SDL_Color white = new SDL.SDL_Color { r = 255, g = 255, b = 255, a = 255 };

        DrawText(renderer, font, "GAZE INTO ABYSS", SCREEN_WIDTH / 2 - 80, SCREEN_HEIGHT / 2 - 50, titleColor);
        
        DrawText(renderer, font, "PRESS [ENTER] TO START NEW ADVANTURE", SCREEN_WIDTH / 2 - 200, SCREEN_HEIGHT / 2 + 20, white);
    }
    static IntPtr BakeTileTextures(IntPtr renderer, float[,] map, int mapWidth, int mapHeight, int tileSize)
    {
        IntPtr texture = SDL.SDL_CreateTexture(
            renderer,
            SDL.SDL_PIXELFORMAT_RGBA8888,
            (int)SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_TARGET,
            mapWidth * tileSize,
            mapHeight * tileSize
        );
        SDL.SDL_SetRenderTarget(renderer, texture);
        SDL.SDL_RenderClear(renderer);

        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0 ; y < mapHeight; y++)
            {
                float value = map[x, y];
                if (value < 0.25f)
                    SDL.SDL_SetRenderDrawColor(renderer, 1, 0, 128, 255); // Deep Water
                else if (value < 0.45f)
                    SDL.SDL_SetRenderDrawColor(renderer, 14, 50, 167, 255); // Medium Water
                else if (value < 0.65f)
                    SDL.SDL_SetRenderDrawColor(renderer, 0, 128, 255, 255); // Shallow Water
                else if (value < 0.75f)
                    SDL.SDL_SetRenderDrawColor(renderer, 115, 194, 251, 255); // Shore
                else if (value < 0.77f)
                    SDL.SDL_SetRenderDrawColor(renderer, 210, 170, 109, 255); // Sand
                else if (value < 0.83f)
                    SDL.SDL_SetRenderDrawColor(renderer, 34, 139, 34, 255); // Grass
                else if (value < 0.9f)
                    SDL.SDL_SetRenderDrawColor(renderer, 61, 61, 61, 255); // Mountain
                else 
                    SDL.SDL_SetRenderDrawColor(renderer, 255, 255, 255, 255); // Snow
                SDL.SDL_Rect rect = new SDL.SDL_Rect{
                    x = x * tileSize,
                    y = y * tileSize,
                    w = tileSize,
                    h = tileSize
                };
                SDL.SDL_RenderFillRect(renderer, ref rect);
            }
        }

        SDL.SDL_SetRenderTarget(renderer, IntPtr.Zero);
        return texture;
    }

    static IntPtr GeneratePermanentFog(IntPtr renderer, int width, int height, int tileSize)
    {
        IntPtr fogTexture = SDL.SDL_CreateTexture(
            renderer,
            SDL.SDL_PIXELFORMAT_RGBA8888,
            (int)SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_TARGET,
            width * tileSize,
            height * tileSize
        );

        SDL.SDL_SetTextureBlendMode(fogTexture, SDL.SDL_BlendMode.SDL_BLENDMODE_BLEND);

        SDL.SDL_SetRenderTarget(renderer, fogTexture);
        SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255);
        SDL.SDL_RenderClear(renderer);
        SDL.SDL_SetRenderTarget(renderer, IntPtr.Zero);
        return fogTexture;
    }

    static IntPtr GenerateDynamicFog(IntPtr renderer, int width, int height, int tileSize)
    {
        IntPtr fogTexture = SDL.SDL_CreateTexture(
            renderer,
            SDL.SDL_PIXELFORMAT_RGBA8888,
            (int)SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_TARGET,
            width * tileSize,
            height * tileSize
        );

        SDL.SDL_SetTextureBlendMode(fogTexture, SDL.SDL_BlendMode.SDL_BLENDMODE_BLEND);

        SDL.SDL_SetRenderTarget(renderer, fogTexture);
        SDL.SDL_SetRenderDrawColor(renderer, 239, 255, 255, 255);
        SDL.SDL_RenderClear(renderer);
        SDL.SDL_SetRenderTarget(renderer, IntPtr.Zero);
        return fogTexture;
    }

    static void FillCircle(IntPtr renderer, int centerX, int centerY, int radius)
    {
        int x = 0;
        int y = radius;
        int d = 3 - 2 * radius;

        while (y >= x)
        {
            SDL.SDL_RenderDrawLine(renderer, centerX - x, centerY + y, centerX + x, centerY + y);
            SDL.SDL_RenderDrawLine(renderer, centerX - x, centerY - y, centerX + x, centerY - y);
            SDL.SDL_RenderDrawLine(renderer, centerX - y, centerY + x, centerX + y, centerY + x);
            SDL.SDL_RenderDrawLine(renderer, centerX - y, centerY - x, centerX + y, centerY - x);

            if (d < 0) d = d + 4 * x + 6;
            else
            {
                d = d + 4 * (x - y) + 10;
                y--;
            }
            x++;
        }
    }

    static void GenerateMap(float[,] map)
    {
        var noise = new FastNoiseLite();
        Random rand = new Random();
        int seed = rand.Next();
        Console.WriteLine($"Noise Seed: {seed}");
        noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        noise.SetSeed(seed);
        noise.SetFrequency(0.005f);
        noise.SetFractalType(FastNoiseLite.FractalType.FBm);
        noise.SetFractalOctaves(5);
        noise.SetFractalLacunarity(2.0f);
        noise.SetFractalGain(0.5f);
        for (int x = 0; x < MAP_WIDTH; x++)
            for (int y = 0; y < MAP_HEIGHT; y++)
                map[x,y] = (noise.GetNoise(x, y) + 1.0f) / 2f;
    }
    static void GenerateFishMap(float[,] fishMap)
    {
        var noise = new FastNoiseLite();
        Random rand = new Random();
        int seed = rand.Next();
        Console.WriteLine($"Fish Noise Seed: {seed}");
        noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        noise.SetSeed(seed);
        noise.SetFrequency(0.02f);
        noise.SetFractalType(FastNoiseLite.FractalType.FBm);
        noise.SetFractalOctaves(5);
        noise.SetFractalLacunarity(2.0f);
        noise.SetFractalGain(0.5f);
        for (int x = 0; x < MAP_WIDTH; x++)
            for (int y = 0; y < MAP_HEIGHT; y++)
                fishMap[x,y] = (noise.GetNoise(x, y) + 1.0f) / 2f;
    }

    public static void DrawText(IntPtr renderer, IntPtr font, string text, int x, int y, SDL.SDL_Color color)
    {
        if (font == IntPtr.Zero) return;

        IntPtr textSurface = SDL_ttf.TTF_RenderUTF8_Blended(font, text, color);
        if (textSurface == IntPtr.Zero) return;

        IntPtr textTexture = SDL.SDL_CreateTextureFromSurface(renderer, textSurface);
        
        SDL.SDL_QueryTexture(textTexture, out _, out _, out int w, out int h);
        SDL.SDL_Rect dstRect = new SDL.SDL_Rect { x = x, y = y, w = w, h = h };

        SDL.SDL_RenderCopy(renderer, textTexture, IntPtr.Zero, ref dstRect);

        SDL.SDL_FreeSurface(textSurface);
        SDL.SDL_DestroyTexture(textTexture);
    }

    static IntPtr GenerateBoat(IntPtr renderer, Player player)
    {   
        IntPtr boatTexture = SDL.SDL_CreateTexture(
            renderer,
            SDL.SDL_PIXELFORMAT_RGBA8888,
            (int)SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_TARGET,
            16,
            16
        );
        SDL.SDL_SetRenderTarget(renderer, boatTexture);
        SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255);
        SDL.SDL_RenderClear(renderer);

        SDL.SDL_SetRenderDrawColor(renderer, 139, 69, 19, 255);
        SDL.SDL_Rect bodyRect = new SDL.SDL_Rect
        {
            x = 0,
            y = 0,
            w = 16,
            h = 16
        };
        SDL.SDL_RenderFillRect(renderer, ref bodyRect);

        SDL.SDL_SetRenderDrawColor(renderer, 255, 255, 255, 255);
        SDL.SDL_Rect sailRect = new SDL.SDL_Rect
        {
            x = 4,
            y = 6,
            w = 10,
            h = 4
        };
        SDL.SDL_RenderFillRect(renderer, ref sailRect);
        SDL.SDL_SetRenderTarget(renderer, IntPtr.Zero); 
        return boatTexture;   
    }
    static IntPtr GeneratePirate(IntPtr renderer)
    {
        IntPtr texture = SDL.SDL_CreateTexture(
            renderer,
            SDL.SDL_PIXELFORMAT_RGBA8888,
            (int)SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_TARGET,
            16, 16
        );

        SDL.SDL_SetRenderTarget(renderer, texture);
        SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255);
        SDL.SDL_RenderClear(renderer);

        SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255);
        SDL.SDL_Rect body = new() { x = 2, y = 2, w = 12, h = 12 };
        SDL.SDL_RenderFillRect(renderer, ref body);

        SDL.SDL_SetRenderDrawColor(renderer, 200, 0, 0, 255);
        SDL.SDL_Rect sail = new() { x = 4, y = 4, w = 8, h = 8 };
        SDL.SDL_RenderFillRect(renderer, ref sail);

        SDL.SDL_SetRenderTarget(renderer, IntPtr.Zero);
        return texture;
    }
    static void DrawHealthBar(IntPtr renderer, float screenX, float screenY, float current, float max)
    {
        int barWidth = 30;
        int barHeight = 4;
        float percent = Math.Clamp(current / max, 0f, 1f);
        int filledWidth = (int)(barWidth * percent);

        SDL.SDL_SetRenderDrawColor(renderer, 50, 50, 50, 200);
        SDL.SDL_Rect bg = new() 
        { 
            x = (int)screenX - barWidth/2 + 7,
            y = (int)screenY,
            w = barWidth,
            h = barHeight
        };
        SDL.SDL_RenderFillRect(renderer, ref bg);
        byte r = (byte)(255 * (1 - percent));
        byte g = (byte)(255 * percent);
        SDL.SDL_SetRenderDrawColor(renderer, r, g, 0, 200);
        SDL.SDL_Rect fill = new() 
        { 
            x = (int)screenX - barWidth/2 + 7, 
            y = (int)screenY, 
            w = filledWidth, 
            h = barHeight 
        };
        SDL.SDL_RenderFillRect(renderer, ref fill);
    }
}

abstract class Boat
{   
    public bool IsDead => currentHealth <= 0;
    public uint lastDamageTime = 0;
    public uint lastShotTime = 0;
    public virtual uint ReloadTime => 2000;
    public float regenRate = 2f;
    public float x, y;
    public float speed;
    public float velocityX = 0f, velocityY = 0f;
    public float currentHealth = 100f, maxHealth = 100f;
    public double angle = 0f;

    public abstract void Update(float[,] map, int mapWidth, int mapHeight, int tileSize, Player player = null);
    public virtual void Spawn(float[,] map, int mapWidth, int mapHeight, int tileSize, Player player = null)
    {
        Random rand = new Random();
        bool found = false;
        while (!found)
        {
            int rx = rand.Next(0, mapWidth);
            int ry = rand.Next(0, mapHeight);
            if (map[rx, ry] < 0.65f) 
            {
                x = rx * tileSize;
                y = ry * tileSize;
                found = true;
            }
        }
    }
    public virtual void TakeDamage(float amount)
    {
        currentHealth -= amount;
        currentHealth = currentHealth < 0 ? 0 : currentHealth;
        lastDamageTime = SDL.SDL_GetTicks();
    }
    public List<Cannonball> ShootLeft()
    {
        if (SDL.SDL_GetTicks() - lastShotTime < ReloadTime) return null;
        lastShotTime = SDL.SDL_GetTicks();

        float shootAngle = (float)(angle - 90);
        return [new Cannonball(x, y, shootAngle, this)];
    }
    public List<Cannonball> ShootRight()
    {
        if (SDL.SDL_GetTicks() - lastShotTime < ReloadTime) return null;
        lastShotTime = SDL.SDL_GetTicks();

        float shootAngle = (float)(angle + 90);
        return [new Cannonball(x, y, shootAngle, this)];
    }   

}

class Player : Boat
{   
    public bool IsFishing { get; set; } = false;
    public bool FishIsBiting { get; set; } = false;
    public bool CannonnsIsAvaible { get; set; } = false;
    public uint BiteTimestamp { get; set; } = 0;
    public int Money { get; set; } = 100000;
    public int MaxInventorySlots { get; set; } = 10; 
    public int Level { get; set; } = 1;
    public float Experience { get; set; } = 0;
    public float ClickStrength { get; set; } = 1.0f;
    public List<Fish> Inventory { get; private set; } = new List<Fish>();
    private uint lastDashTime = 0;
    private const uint DASH_COOLDOWN = 5000;
    public Player()
    {
        x = 0f;
        y = 0f;
        speed = 10f;
    }
    public Player(float[,] map, int mapWidth, int mapHeight, int tileSize, float speed)
    {
        Spawn(map, mapWidth, mapHeight, tileSize);
        this.speed = speed;
    }
    public override void Update(float[,] map, int mapWidth, int mapHeight, int tileSize, Player player = null)
    {   
        if (IsFishing)
            return;
        IntPtr keyState = SDL.SDL_GetKeyboardState(out _);
        float friction = 0.92f;

        unsafe
        {
            byte* keys = (byte*)keyState;

            if (keys[(int)SDL.SDL_Scancode.SDL_SCANCODE_W] != 0) velocityY -= speed;
            if (keys[(int)SDL.SDL_Scancode.SDL_SCANCODE_S] != 0) velocityY += speed;
            if (keys[(int)SDL.SDL_Scancode.SDL_SCANCODE_A] != 0) velocityX -= speed;
            if (keys[(int)SDL.SDL_Scancode.SDL_SCANCODE_D] != 0) velocityX += speed;
        }

        velocityX *= friction;
        velocityY *= friction;

        float moveX = velocityX;
        float moveY = velocityY;

        if (Math.Abs(velocityX) > 0.1 || Math.Abs(velocityY) > 0.1) 
        {
            angle = Math.Atan2(velocityY, velocityX) * (180.0 / Math.PI);
        }

        float nextX = x + moveX;
        float nextY = y + moveY;

        int mapX= (int)(nextX / tileSize);
        int mapY= (int)(nextY / tileSize);
        
        if (mapX >= 0 && mapX < mapWidth && mapY >= 0 && mapY < mapHeight)
        {   
            if (map[mapX, mapY] < 0.75f)
            {
                x = nextX;
                y = nextY;
            }
            else
            {   
                velocityX = 0f;
                velocityY = 0f;
                Console.WriteLine("Sand! Can't swimm on sand.");
            }
        }
        uint now = SDL.SDL_GetTicks();
        if (now - lastDamageTime > 10000)
        {
            currentHealth = Math.Min(maxHealth, currentHealth + regenRate * 0.016f);
        }
    }

    public void Dash()
    {
        if (SDL.SDL_GetTicks() - lastDashTime < DASH_COOLDOWN) return;
        
        velocityX *= 5f;
        velocityY *= 5f;
        lastDashTime = SDL.SDL_GetTicks();
    }

    public override void Spawn(float [,] map, int mapWidth, int mapHeight, int tileSize, Player player = null)
    {      
        bool found = false;

        int centerX = mapWidth / 2;
        int centerY = mapWidth / 2;
        int searchRadius = 0;
        int maxSearchRadius = Math.Max(mapWidth, mapHeight) / 2;

        while (!found && searchRadius < maxSearchRadius)
        {
            for (int dx = -searchRadius; dx <= searchRadius; dx++)
            {
                for (int dy = -searchRadius; dy <= searchRadius; dy++)
                {
                    if (Math.Abs(dx) != searchRadius && Math.Abs(dy) != searchRadius) continue;

                    int x = centerX + dx;
                    int y = centerY + dy;

                    if ( x >= 0 && x < mapWidth && y >= 0 && y < mapHeight)
                    {
                        if (map[x, y] >= 0.65f && map[x, y] < 0.75f)
                        {
                            if (LandNearby(map, x, y, mapWidth, mapHeight))
                            {
                                this.x = x * tileSize + tileSize / 2;
                                this.y = y * tileSize + tileSize / 2;
                                found = true;
                                break;
                            }
                        }
                    }
                }   if (found) break;
            }
            searchRadius++;
        }
        
        if (!found)
        {   
            this.x = (mapWidth * tileSize) / 2;
            this.y = (mapHeight * tileSize) / 2;
            Console.WriteLine("Failed to find spawn point, defaulting to center.");
        }  
    }
    public void Respawn(float[,] map, int mapWidth, int mapHeight, int tileSize)
    {
        Money = (int)(Money * 0.75f);

        Inventory.Clear();

        currentHealth = maxHealth;

        IsFishing = false;
        FishIsBiting = false;

        Spawn(map, mapWidth, mapHeight, tileSize);
    }
    public bool AddToInventory(Fish fish)
    {
        if (Inventory.Count < MaxInventorySlots)
        {   
            Experience += 10 * fish.RarityMultiplier;
            Inventory.Add(fish);
            CheckLevelUp();
            return true;
        }
        return false; 
    }
    private void CheckLevelUp()
    {
        if (Experience >= Level * 100)
        {
            Experience -= Level * 100;
            Level++;
            Console.WriteLine($"УРОВЕНЬ ПОВЫШЕН! Теперь ваш уровень: {Level}");
        }
    }

    private bool LandNearby(float[,] map, int x, int y, int w, int h)
    {
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                int nx = x + i;
                int ny = y + j;
                if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                {
                    if (map[nx, ny] >= 0.75f) return true;
                }
            }
        }
        return false;
    }
}

public enum PirateState { Patrol, Attack, FLee }

class Pirate : Boat
{
    public PirateState currentState = PirateState.Patrol;
    public override uint ReloadTime => 3000;
    public (float x, float y) ? SpawnPoint { get; set; }
    public uint? DeathTime { get; set; }
    public const uint RESPAWN_TIME = 10000;
    protected const uint PATROL_CHANGE_INTERVAL = 1500;
    protected uint lastPatrolChange = 0;
    protected float patrolTargetX, patrolTargetY;
    protected float viewDistance = 200f;
    protected float desiredDistance = 70f;

    public Pirate(float startX, float startY, float health)
    {
        x = startX;
        y = startY;
        currentHealth = health;
        maxHealth = health;
        speed = 0.1f;
        angle = new Random().NextDouble() * 360;
    }
    public override void Update(float[,] map, int mapWidth, int mapHeight, int tileSize, Player player = null)
    {   
        if (player == null) return;
        if (IsDead) return;
        float dx = player.x - x;
        float dy = player.y - y;
        float distance = (float)Math.Sqrt(dx * dx + dy * dy);

        int pMapX = (int)(player.x / tileSize);
        int pMapY = (int)(player.y / tileSize);
        bool playerInSafeZone = false;

        if (pMapX >= 0 && pMapX < mapWidth && pMapY >= 0 && pMapY < mapHeight)
            playerInSafeZone = map[pMapX, pMapY] >= 0.65f;
        else playerInSafeZone = true;

        if (distance > viewDistance * 1.5f || playerInSafeZone)
        {
            currentState = PirateState.Patrol;
        }
        else if (distance < viewDistance)
        {
            if (currentHealth < maxHealth * 0.25f)
            {
                currentState = PirateState.FLee;
            }
            else
            {
                currentState = PirateState.Attack;
            }
        }


        float nx = dx / (distance > 0 ? distance : 1);
        float ny = dy / (distance > 0 ? distance : 1);

        switch (currentState)
        {
            case PirateState.Patrol:
                UpdatePatrol(mapWidth, mapHeight, tileSize);                
                break;

            case PirateState.Attack:
                float orbitX = -ny; 
                float orbitY = nx;

                float attractionStrength = 0.4f;
                float approachX = 0;
                float approachY = 0;

                if (distance > desiredDistance + 20) 
                {
                    approachX = nx; approachY = ny;
                } 
                else if (distance < desiredDistance - 20) 
                {
                    approachX = -nx; approachY = -ny;
                }

                float finalDirX = (orbitX * 0.5f) + (approachX * attractionStrength);
                float finalDirY = (orbitY * 0.5f) + (approachY * attractionStrength);

                velocityX += finalDirX * speed;
                velocityY += finalDirY * speed;
                break;

            case PirateState.FLee:
                velocityX -= nx * speed * 0.8f;
                velocityY -= ny * speed * 0.8f;
                break;
        }
        
        ApplyPhysics(map, mapWidth, mapHeight, tileSize);
    }
    public override void Spawn(float[,] map, int mapWidth, int mapHeight, int tileSize, Player player = null)
    {   
        if (player == null) return;

        Random rand = new Random();
        bool found = false;
        int attempts = 0;

        while (!found && attempts < 100)
        {
            attempts++;
            int rx = rand.Next(0, mapWidth);
            int ry = rand.Next(0, mapHeight);

            if (map[rx, ry] < 0.65f)
            {   
                float worldX = rx * tileSize;
                float worldY = ry * tileSize;
                float dx = worldX - player.x;
                float dy = worldY - player.y;
                float dist = dx * dx + dy * dy;
                if (dist > 40000)
                {
                    x = worldX;
                    y = worldY;
                    found = true;
                    SetRandomPatrolTarget(mapWidth, mapHeight, tileSize);
                }
            }
        }
    }
    public bool ShouldRespawn(uint currentTime)
    {
        return DeathTime.HasValue && (currentTime - DeathTime.Value > RESPAWN_TIME);
    }
    public void Respawn()
    {
        if (SpawnPoint.HasValue)
        {
            x = SpawnPoint.Value.x;
            y = SpawnPoint.Value.y;
            currentHealth = maxHealth;
            DeathTime = null;
            currentState = PirateState.Patrol;
        }
    }
    private void UpdatePatrol(int mapWidth, int mapHeight, int tileSize)
    {
        uint now = SDL.SDL_GetTicks();
        if (now - lastPatrolChange > PATROL_CHANGE_INTERVAL)
            SetRandomPatrolTarget(mapWidth, mapHeight, tileSize);

        float dX = patrolTargetX - x;
        float dY = patrolTargetY - y;
        float dist = (float)Math.Sqrt(dX * dX + dY * dY);

        if (dist > 10f) {
            velocityX += (dX / dist) * speed * 0.5f;
            velocityY += (dY / dist) * speed * 0.5f;
        }
    }
    private void SetRandomPatrolTarget(int mapWidth, int mapHeight, int tileSize)
    {
        Random rand = new Random();
        patrolTargetX = rand.Next(0, mapWidth) * tileSize;
        patrolTargetY = rand.Next(0, mapHeight) * tileSize;
        lastPatrolChange = SDL.SDL_GetTicks();
    }
    private void ApplyPhysics(float[,] map, int mapWidth, int mapHeight, int tileSize)
    {
        float friction = 0.95f;
        velocityX *= friction;
        velocityY *= friction;

        if (Math.Abs(velocityX) > 0.1 || Math.Abs(velocityY) > 0.1) angle = Math.Atan2(velocityY, velocityX) * (180.0 / Math.PI);

        float nextX = x + velocityX;
        float nextY = y + velocityY;

        int mX = (int)(nextX / tileSize);
        int mY = (int)(nextY / tileSize);

        if (mX <= 0 || mX >= mapWidth - 1 || mY <= 0 || mY >= mapHeight - 1 || map[mX, mY] >= 0.65f)
        {
            velocityX *= -1.2f; 
            velocityY *= -1.2f;
            
            x += velocityX;
            y += velocityY;
            
            angle = Math.Atan2(velocityY, velocityX) * 180.0 / Math.PI;
        }
        else
        {
            x = nextX;
            y = nextY;
        }
    }
    public List<Cannonball> TryShootAt(Player player)
    {
        if (player == null) return null;

        double angleToPlayer = Math.Atan2(player.y - y, player.x - x) * 180.0 / Math.PI;

        double leftBort = NormalizeAngle(angle - 90);
        double rightBort = NormalizeAngle(angle + 90);

        double diffToLeft = Math.Abs(NormalizeAngle(angleToPlayer - leftBort));
        double diffToRight = Math.Abs(NormalizeAngle(angleToPlayer - rightBort));

        return diffToLeft < diffToRight ? ShootLeft() : ShootRight();       
    }

    private static double NormalizeAngle(double a)
    {
        while (a > 180) a -= 360;
        while (a <= -180) a += 360;
        return a;
    }
}

class PirateKing : Pirate
{
    public const float KING_MAX_HEALTH = 1000f;
    public static bool IsDefeated = false;
    public bool HasBeenSeen { get; set; } = false;
    public override uint ReloadTime => 1500;
    public PirateKing(float x, float y) : base(x, y, KING_MAX_HEALTH)
    {
        currentHealth = KING_MAX_HEALTH;
        maxHealth = KING_MAX_HEALTH;
        speed = 0.06f;
    }

    public override void Spawn(float[,] map, int mapWidth, int mapHeight, int tileSize, Player player = null)
    {
        // 1. Находим все области бездны (Deep Water < 0.25f)
        bool[,] visited = new bool[mapWidth, mapHeight];
        int maxRegionSize = 0;
        List<(int x, int y)> bestRegion = new List<(int, int)>();

        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
            {
                // Если нашли клетку бездны, которую еще не посещали
                if (map[x, y] < 0.25f && !visited[x, y])
                {
                    List<(int x, int y)> currentRegion = new List<(int, int)>();
                    Queue<(int x, int y)> queue = new Queue<(int, int)>();
                    
                    queue.Enqueue((x, y));
                    visited[x, y] = true;

                    // Алгоритм заливки (Flood Fill) для поиска размера области
                    while (queue.Count > 0)
                    {
                        var cell = queue.Dequeue();
                        currentRegion.Add(cell);

                        // Проверяем 4 соседние клетки
                        int[] dx = { 0, 0, 1, -1 };
                        int[] dy = { 1, -1, 0, 0 };

                        for (int i = 0; i < 4; i++)
                        {
                            int nx = cell.x + dx[i];
                            int ny = cell.y + dy[i];

                            if (nx >= 0 && nx < mapWidth && ny >= 0 && ny < mapHeight &&
                                !visited[nx, ny] && map[nx, ny] < 0.25f)
                            {
                                visited[nx, ny] = true;
                                queue.Enqueue((nx, ny));
                            }
                        }
                    }

                    // Если эта область больше предыдущей максимальной — запоминаем её
                    if (currentRegion.Count > maxRegionSize)
                    {
                        maxRegionSize = currentRegion.Count;
                        bestRegion = currentRegion;
                    }
                }
            }
        }

        // 2. Спавним короля в центре найденной области
        if (bestRegion.Count > 0)
        {
            // Выбираем среднюю точку области для спавна
            var spawnCell = bestRegion[bestRegion.Count / 2];
            this.x = spawnCell.x * tileSize + (tileSize / 2);
            this.y = spawnCell.y * tileSize + (tileSize / 2);
            this.SpawnPoint = (this.x, this.y);
            
            Console.WriteLine($"Король Пиратов пробудился в крупнейшей бездне: {spawnCell.x}, {spawnCell.y} (Размер: {maxRegionSize} клеток)");
        }
        else
        {
            // Резервный вариант, если бездны нет (хотя при шуме она должна быть)
            Console.WriteLine("Бездна не найдена! Спавн в центре мира.");
            base.Spawn(map, mapWidth, mapHeight, tileSize, player);
        }
    }

    public static IntPtr CreateTexture(IntPtr renderer)
    {
        IntPtr texture = SDL.SDL_CreateTexture(
            renderer,
            SDL.SDL_PIXELFORMAT_RGBA8888,
            (int)SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_TARGET,
            32, 48
        );

        SDL.SDL_SetRenderTarget(renderer, texture);
        SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 0);
        SDL.SDL_RenderClear(renderer);

        SDL.SDL_SetRenderDrawColor(renderer, 60, 30, 10, 255);
        SDL.SDL_Rect body = new() { x = 4, y = 2, w = 24, h = 44 };
        SDL.SDL_RenderFillRect(renderer, ref body);

        SDL.SDL_SetRenderDrawColor(renderer, 218, 165, 32, 255);
        SDL.SDL_RenderDrawLine(renderer, 4, 2, 4, 46);
        SDL.SDL_RenderDrawLine(renderer, 28, 2, 28, 46);

        SDL.SDL_SetRenderDrawColor(renderer, 255, 215, 0, 255);
        SDL.SDL_Rect sailMain = new() { x = 6, y = 15, w = 20, h = 10 };
        SDL.SDL_Rect sailFront = new() { x = 10, y = 5, w = 12, h = 6 };
        SDL.SDL_Rect sailBack = new() { x = 10, y = 32, w = 12, h = 6 };
        SDL.SDL_RenderFillRect(renderer, ref sailMain);
        SDL.SDL_RenderFillRect(renderer, ref sailFront);
        SDL.SDL_RenderFillRect(renderer, ref sailBack);

        SDL.SDL_SetRenderTarget(renderer, IntPtr.Zero);

        return texture;        
    }
    public List<Cannonball> TripleShootAt(Player player)
    {
        if (player == null || SDL.SDL_GetTicks() - lastShotTime < ReloadTime) return null;
        
        double angleToPlayer = Math.Atan2(player.y - y, player.x - x) * 180.0 / Math.PI;

        double diff = NormalizeAngle(angleToPlayer - angle);

        double shootAngle = (diff > 0) ? angle + 90 : angle - 90;

        lastShotTime = SDL.SDL_GetTicks();
        List<Cannonball> burst = new List<Cannonball>();

        burst.Add(new Cannonball(x, y, (float)shootAngle - 12, this));
        burst.Add(new Cannonball(x, y, (float)shootAngle, this));
        burst.Add(new Cannonball(x, y, (float)shootAngle + 12, this));

        return burst;
    }

    private double NormalizeAngle(double a)
    {
        while (a > 180) a -= 360;
        while (a <= -180) a += 360;
        return a;
    }
}

class Cannonball
{
    public uint spawnTime;
    public uint lifeTime = 5000;
    public float x, y;
    public float velocityX, velocityY;
    public float baseDmg = 50f;
    public float speed = 200f;
    public const float airResistance = 0.98f;
    public Boat owner;
    public bool IsSink { get; private set; } = false;
    public float InitialSpeed { get; private set; }

    public Cannonball(float startX, float startY, float angle, Boat owner)
    {
        x = startX;
        y = startY;
        this.owner = owner;
        spawnTime = SDL.SDL_GetTicks();

        float rad = angle * (float)Math.PI / 180f;
        velocityX = (float)Math.Cos(rad) * speed;
        velocityY = (float)Math.Sin(rad) * speed;
        InitialSpeed = speed;
    }

    public bool IsExpired()
    {
        return IsSink || SDL.SDL_GetTicks() - spawnTime > lifeTime;
    }

    public void Update(float[,] map, int mapWidth, int mapHeight, int tileSize)
    {
        if (IsSink) return;

        velocityX *= airResistance;
        velocityY *= airResistance;

        x += velocityX * 0.016f;
        y += velocityY * 0.016f;

        int tileX = (int)(x / tileSize);
        int tileY = (int)(y / tileSize);

        bool inBounds = tileX >= 0 && tileX < mapWidth && tileY >= 0 && tileY < mapHeight;

        float currentSpeed = (float)Math.Sqrt(velocityX * velocityX + velocityY * velocityY);


        if (!inBounds)
        {
            IsSink = true;
            return;
        }

        float terrain = map[tileX, tileY];

        if (currentSpeed < 20f)
        {
            if (terrain < 0.75f)
            {
                IsSink = true;
            }
            else if (terrain >= 0.75)
            {
                velocityX = 0;
                velocityY = 0;
                speed = 0;
            }
        }
    }

    public float GetCurrentDmg()
    {
        float currentSpeed = (float)Math.Sqrt(velocityX * velocityX + velocityY * velocityY);
        return baseDmg * currentSpeed / InitialSpeed;
    }

    public bool CheckCollisionWith(Boat target)
    {
        if (target == null || target == owner || IsSink) return false;
        if (owner is Pirate && target is Pirate) return false;

        float dx = x - target.x;
        float dy = y - target.y;
        float dist = dx * dx + dy * dy;
        return dist < 400f;
    }
}

class DamagePopup : Game
{
    public float x, y;
    public string text;
    public uint spawnTime;
    public const uint LIFETIME_MS = 1000;

    public DamagePopup(float worldX, float worldY, float damage)
    {
        x = worldX;
        y = worldY;
        text = ((int)damage).ToString();
        spawnTime = SDL.SDL_GetTicks();
    }

    public bool IsExpired() => SDL.SDL_GetTicks() - spawnTime > LIFETIME_MS;

    public void Draw(IntPtr renderer, IntPtr font, float camX, float camY)
    {
        float alpha = 1.0f - (float)(SDL.SDL_GetTicks() - spawnTime) / LIFETIME_MS;
        byte a = (byte)(255 * alpha);
        SDL.SDL_Color color = new() { r = 255, g = 50, b = 50, a = a };

        DrawText(renderer, font, text, (int)(x - camX), (int)(y - camY - 30), color);
    }
}

public enum FishRarity
{
    Common,
    Rare,
    Epic,
    Legendary

}

class FishData 
{
    public string Name { get; set; }
    public int MinLevel { get; set; }
    public int Rarity { get; set; } 
    public float BaseWeight { get; set; }
}

class Fish
{
    public string Name { get; set; }
    public float Weight { get; set; }
    public int Price { get; set; }
    public int Experience { get; set; }
    public int RequiredLevel { get; set; }
    public FishRarity Rarity { get; set; }
    public float RarityMultiplier {get; set; }
    private static List<FishData> _allFishTypes;


    public Fish(string name, int requiredLevel, FishRarity rarity, float baseWeight)
    {
        Name = name;
        RequiredLevel = requiredLevel;
        Rarity = rarity;
        GenerateStats(baseWeight);
    }

    public static void LoadFishData(string path) 
    {
        string json = File.ReadAllText(path);
        var root = JsonSerializer.Deserialize<Dictionary<string, List<FishData>>>(json);
        _allFishTypes = root["fishes"];
    }

    public static Fish GenerateFishAt(float depth, float fishNoise, int playerLevel)
    {
        FishRarity targetRarity = FishRarity.Common;
        if (depth < 0.25f) targetRarity = (fishNoise > 0.8f) ? FishRarity.Legendary : FishRarity.Epic;
        else if (depth < 0.5f) targetRarity = (fishNoise > 0.7f) ? FishRarity.Rare : FishRarity.Common;

        var possibleFishes = _allFishTypes.Where(f => (int)f.Rarity == (int)targetRarity && f.MinLevel <= playerLevel).ToList();
        
        if (possibleFishes.Count == 0) 
            possibleFishes = _allFishTypes.Where(f => f.Rarity == 0).ToList();
        
        if (possibleFishes.Count == 0)
        {
            return new Fish("Старый башмак", 1, FishRarity.Common, 0.1f);
        }
        var selected = possibleFishes[new Random().Next(possibleFishes.Count)];
        return new Fish(selected.Name, selected.MinLevel, (FishRarity)selected.Rarity, selected.BaseWeight);
    }

    private void GenerateStats(float baseWeight)
    {
        Random rand = new Random();
        RarityMultiplier = GetRarityMultiplier();
        double rawWeight = baseWeight + (rand.NextDouble() * 1.5 * RarityMultiplier);    
        Weight = (float)Math.Round(rawWeight, 3);
        Price = (int)(Weight * 20 * RarityMultiplier);
        Experience = (int)(10 * RarityMultiplier);
    }

    private float GetRarityMultiplier()
    {
        return Rarity switch
        {
            FishRarity.Common => 1.0f,
            FishRarity.Rare => 2f,
            FishRarity.Epic => 5.0f,
            FishRarity.Legendary => 15.0f,
            _ => 1.0f,
        };
    }
}

class FishingGame
{
    public float Tension { get; private set; } = 0f;   
    public float Progress { get; private set; } = 0f;   
    public float MaxProgress { get; private set; }    
    public bool IsActive { get; set; } = false;
    public bool Loss { get; set; } = false;
    
    private float fishPower; 
    private Random rand = new Random();

    public void Start(Fish fish)
    {
        Tension = 0f;
        Progress = 0f;
        MaxProgress = fish.Weight * 100f;
        fishPower = 0.15f + ((int)fish.Rarity * 0.1f); 
        IsActive = true;
    }

    public void Update()
    {   
        if (!IsActive) return;
        Tension -= 0.25f * fishPower;
    }

    public void PlayerClick(float clickStrength = 1.0f)
    {
        Tension += 1.0f; 
        Progress += 10f * clickStrength;
    }

    public bool CheckWin() => Progress >= MaxProgress;
    public bool CheckLoss() => Math.Abs(Tension) >= 10f;
    public bool Loose() => Loss = true;
}

class Map
{   
    public bool isFullScreen = false;
    public void Draw(IntPtr renderer, float[,] map, float[,] fishMap, bool[,] discovered, Player player, PirateKing pirateKing, Map miniMap, int screenWidth, int screenHeight, int mapWidth, int mapHeight, int tileSize)
    {
        int mapSizeX, mapSizeY, posX, posY;

        if (miniMap.isFullScreen)
        {   
            mapSizeX = (int)(screenWidth * 0.8f);
            mapSizeY = (int)(screenHeight * 0.8f);
            posX = (screenWidth - mapSizeX) / 2;
            posY = (screenHeight - mapSizeY) / 2;
        }
        else
        {   
            mapSizeX = 200;
            mapSizeY = 150;
            posX = screenWidth - mapSizeX - 25;
            posY = screenHeight - mapSizeY - 25;
        }

        SDL.SDL_Rect bg = new SDL.SDL_Rect { x = posX - 5, y = posY - 5, w = mapSizeX + 10, h = mapSizeY +10 };
        SDL.SDL_SetRenderDrawColor(renderer, 224, 211, 175, 255);
        SDL.SDL_RenderFillRect(renderer, ref bg);

        int startX = miniMap.isFullScreen ? 0 : (int)(player.x / tileSize) - 40;
        int endX = miniMap.isFullScreen ? mapWidth : (int)(player.x / tileSize) + 40;
        int startY = miniMap.isFullScreen ? 0 : (int)(player.y / tileSize) - 40;
        int endY = miniMap.isFullScreen ? mapHeight : (int)(player.y / tileSize) + 40;

        int totalVisibleX = endX - startX;
        int totalVisibleY = endY - startY;

        float tileSizeX = (float)mapSizeX / totalVisibleX;
        float tileSizeY = (float)mapSizeY / totalVisibleY;

        for (int x = startX; x < endX; x++)
        {
            for(int y = startY; y < endY; y++)
            {
                if (x >= 0 && x < mapWidth && y >= 0 && y < mapHeight)
                {
                    SDL.SDL_Rect tileRect = new SDL.SDL_Rect
                    {
                        x = posX + (int)((x - startX) * tileSizeX),
                        y = posY + (int)((y - startY) * tileSizeY),
                        w = (int)tileSizeX + 1,
                        h = (int)tileSizeY + 1
                    };

                    if (discovered[x, y])
                    {
                        SetColorByDepth(renderer, map[x, y]);
                        SDL.SDL_RenderFillRect(renderer, ref tileRect);
                    }
                    else
                    {
                        SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255);
                        SDL.SDL_RenderFillRect(renderer, ref tileRect);
                    }
                }
            }
        }
        float pX_ratio = (float)(player.x / 10 - startX) / totalVisibleX;
        float pY_ratio = (float)(player.y / 10 - startY) / totalVisibleY;

        SDL.SDL_SetRenderDrawColor(renderer, 255, 50, 50, 255);
        SDL.SDL_Rect playerMarker = new SDL.SDL_Rect {
            x = posX + (int)(pX_ratio * mapSizeX) - 3,
            y = posY + (int)(pY_ratio * mapSizeY) - 3,
            w = 6, h = 6
        };
        SDL.SDL_RenderFillRect(renderer, ref playerMarker);

        if (pirateKing.HasBeenSeen && !pirateKing.IsDead)
        {
            float bossGridX = pirateKing.x / tileSize; 
            float bossGridY = pirateKing.y / tileSize;

            if (bossGridX >= startX && bossGridX <= endX && bossGridY >= startY && bossGridY <= endY)
            {
                float bX_ratio = (bossGridX - startX) / totalVisibleX;
                float bY_ratio = (bossGridY - startY) / totalVisibleY;

                SDL.SDL_SetRenderDrawColor(renderer, 255, 215, 0, 255); 
                SDL.SDL_Rect bossMarker = new SDL.SDL_Rect {
                    x = posX + (int)(bX_ratio * mapSizeX) - 5,
                    y = posY + (int)(bY_ratio * mapSizeY) - 5,
                    w = 10, h = 10
                };
                SDL.SDL_RenderFillRect(renderer, ref bossMarker);
            }
        }
    }
    private static void SetColorByDepth(IntPtr renderer, float value)
    {
        if (value < 0.25f)
            SDL.SDL_SetRenderDrawColor(renderer, 1, 0, 128, 255); // Deep Water
        else if (value < 0.45f)
            SDL.SDL_SetRenderDrawColor(renderer, 14, 50, 167, 255); // Medium Water
        else if (value < 0.65f)
            SDL.SDL_SetRenderDrawColor(renderer, 0, 128, 255, 255); // Shallow Water
        else if (value < 0.75f)
            SDL.SDL_SetRenderDrawColor(renderer, 115, 194, 251, 255); // Shore
        else if (value < 0.77f)
            SDL.SDL_SetRenderDrawColor(renderer, 210, 170, 109, 255); // Sand
        else if (value < 0.83f)
            SDL.SDL_SetRenderDrawColor(renderer, 34, 139, 34, 255); // Grass
        else if (value < 0.9f)
            SDL.SDL_SetRenderDrawColor(renderer, 61, 61, 61, 255); // Mountain
        else 
            SDL.SDL_SetRenderDrawColor(renderer, 255, 255, 255, 255); // Snow
    }
    public void Open()
    {
        isFullScreen = true;
    }
    public void Close()
    {
        isFullScreen = false;
    }
}

class ShopItem 
{
    public string Name;
    public int Cost;
    public Action<Player> ApplyUpgrade;
}

class Shop : Game
{
    public static List<ShopItem> GetAvailableItems(Player player)
    {
        var items = new List<ShopItem>();

        if (player.speed < 0.8f)
            items.Add(new ShopItem
            {
                Name = $"Speed up (+0.1) — {300 * (int)(player.speed * 10)} coins",
                Cost = 300 * (int)(player.speed * 10),
                ApplyUpgrade = p => p.speed += 0.1f
            });

        if (player.MaxInventorySlots < 30)
            items.Add(new ShopItem
            {
                Name = $"+1 quantity of cargo — {100 * player.MaxInventorySlots} coins",
                Cost = 100 * player.MaxInventorySlots,
                ApplyUpgrade = p => p.MaxInventorySlots++
            });

        if (player.maxHealth < 500f)
            items.Add(new ShopItem
            {
                Name = $"+50 HP — {300} coins",
                Cost = 300,
                ApplyUpgrade = p => { p.maxHealth += 50; p.currentHealth = p.maxHealth; }
            });

        if (!player.CannonnsIsAvaible)
            items.Add(new ShopItem
            {
                Name = "Unlock cannons — 500 coins",
                Cost = 500,
                ApplyUpgrade = p => p.CannonnsIsAvaible = true
            });
        if (player.Inventory.Count > 0)
        {
            int totalValue = player.Inventory.Sum(f => f.Price);
            items.Add(new ShopItem
            {
                Name = $"Sell fish ({player.Inventory.Count}) — {totalValue} coins",
                Cost = 0,
                ApplyUpgrade = p =>
                {
                    p.Money += totalValue;
                    p.Inventory.Clear();
                }
            });
        }
        return items;
    }

    public static void DrawShop(IntPtr renderer, IntPtr font, List<ShopItem> items, int selected, float camX, float camY)
    {
        SDL.SDL_SetRenderDrawColor(renderer, 30, 30, 50, 200);
        SDL.SDL_Rect bg = new() { x = 100, y = 100, w = 600, h = 400 };
        SDL.SDL_RenderFillRect(renderer, ref bg);

        DrawText(renderer, font, "NEAR PIER SHOP", 300, 120, new SDL.SDL_Color { r = 255, g = 215, b = 0, a = 255 });

        for (int i = 0; i < items.Count; i++)
        {
            SDL.SDL_Color color = (i == selected) ? 
                new SDL.SDL_Color { r = 255, g = 255, b = 255, a = 255 } :
                new SDL.SDL_Color { r = 200, g = 200, b = 200, a = 255 };

            DrawText(renderer, font, items[i].Name, 150, 160 + i * 30, color);
        }

        DrawText(renderer, font, "ESC - Exit", 150, 160 + items.Count * 30 + 10, new SDL.SDL_Color { r = 200, g = 200, b = 200, a = 255 });
    }
}