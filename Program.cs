using SDL2;
using System;
using System.Data;
using System.IO;
using System.Collections.Generic;
using System.Globalization;


internal class Program
{
    static void Main()
    {
        Game.Start();
    }
}

class Game 
{   
    enum GameState {Menu, Playing, Settings}
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

        Player player = new Player(map, MAP_WIDTH, MAP_HEIGHT, TILE_SIZE, 0.1f);

        IntPtr boatTexture = GenerateBoat(renderer, player);

        if (!pierInitialized)
        {
            pierX = player.X - 20;
            pierY = player.Y - 20;
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
                    if (e.type == SDL.SDL_EventType.SDL_KEYDOWN)
                    {
                        int pX = (int)(player.X / TILE_SIZE);
                        int pY = (int)(player.Y / TILE_SIZE);
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
                        if (e.key.keysym.scancode == SDL.SDL_Scancode.SDL_SCANCODE_F)
                        {
                            float dx = player.X - pierX;
                            float dy = player.Y - pierY;
                            float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                            if (distance < 40f && player.Inventory.Count > 0)
                            {   
                                int TotalMoney = 0;
                                foreach (var fish in player.Inventory)
                                {
                                    Console.WriteLine($"Вы продали {fish.Name} весом {fish.Weight} за {fish.Price} монет!");
                                    TotalMoney += fish.Price;
                                }
                                player.Money += TotalMoney;
                                player.Inventory.Clear();
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
                                    fishMap[pX, pY]
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
            else
            {
                Player.Move(player, map, MAP_WIDTH, MAP_HEIGHT, TILE_SIZE);

                float wave = (float)Math.Sin(ticks * 0.0001f);
                byte colorMod = (byte)(215 + (wave * 40));
                SDL.SDL_SetTextureColorMod(worldTexture, colorMod, colorMod, 255);


                camX = player.X - (SCREEN_WIDTH / 2);
                camY = player.Y - (SCREEN_HEIGHT / 2);

                camX = Math.Clamp(camX, 0, (MAP_WIDTH * TILE_SIZE) - SCREEN_WIDTH);
                camY = Math.Clamp(camY, 0, (MAP_HEIGHT * TILE_SIZE) - SCREEN_HEIGHT);

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

                SDL.SDL_SetRenderTarget(renderer, fogTexture);
                SDL.SDL_SetRenderDrawBlendMode(renderer, SDL.SDL_BlendMode.SDL_BLENDMODE_NONE);
                SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 0);

                FillCircle(renderer, (int)player.X, (int)player.Y, 200);

                SDL.SDL_SetRenderTarget(renderer, dynamicFogTexture);
                SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 180);
                SDL.SDL_RenderClear(renderer);

                SDL.SDL_SetRenderDrawBlendMode(renderer, SDL.SDL_BlendMode.SDL_BLENDMODE_NONE);
                SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 0);

                FillCircle(renderer, (int)player.X, (int)player.Y, 180);

                SDL.SDL_SetRenderTarget(renderer, IntPtr.Zero);

                SDL.SDL_RenderCopy(renderer, dynamicFogTexture, ref srcRect, ref destRect);

                SDL.SDL_RenderCopy(renderer, fogTexture, ref srcRect, ref destRect);

                int playerX = (int)(player.X / TILE_SIZE);
                int playerY = (int)(player.Y / TILE_SIZE);
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
                    x = (int)(player.X - camX),
                    y = (int)(player.Y - camY),
                    w = 16,
                    h = 16
                };

                SDL.SDL_RenderCopyEx(
                    renderer, 
                    boatTexture, 
                    IntPtr.Zero, 
                    ref playerRect, 
                    player.Angle, 
                    IntPtr.Zero, 
                    SDL.SDL_RendererFlip.SDL_FLIP_NONE
                );

                SDL.SDL_SetRenderDrawColor(renderer, 101, 67, 33, 255);
                SDL.SDL_Rect pierRect = new SDL.SDL_Rect
                {
                    x = (int)(pierX - camX - 10),
                    y = (int)(pierY - camY - 5),
                    w = 30,
                    h = 30
                };
                SDL.SDL_RenderFillRect(renderer, ref pierRect);

                float distToPier = (float)Math.Sqrt(Math.Pow(player.X - pierX, 2) + Math.Pow(player.Y - pierY, 2));
                if (distToPier < 50f && player.Inventory.Count() > 0) {
                    SDL.SDL_Color gold = new SDL.SDL_Color { r = 255, g = 215, b = 0, a = 255};
                    DrawText(renderer, font, "PRESS [F] TO SELL FISH", (int)(pierX - camX - 100), (int)(pierY - camY - 30), gold);
                }
                
                int pTileX = (int)(player.X / TILE_SIZE);
                int pTileY = (int)(player.Y / TILE_SIZE);

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
                        int pX = (int)(player.X / TILE_SIZE);
                        int pY = (int)(player.Y / TILE_SIZE);

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

                DrawText(renderer, font, $"GOLD: {player.Money}", 25, 20, white);
                DrawText(renderer, font, $"LVL: {player.Level}", 25, 40, white);
                DrawText(renderer, font, $"EXP: {player.Experience}/{player.Level * 100}", 100, 40, white);
                DrawText(renderer, font, $"CARGO: {player.Inventory.Count}/{player.MaxInventorySlots}", 25, 60, white);
                miniMap.Draw(renderer, map, fishMap, discovered, player, miniMap, SCREEN_WIDTH, SCREEN_HEIGHT, MAP_WIDTH, MAP_HEIGHT);

            }

            SDL.SDL_RenderPresent(renderer);
            SDL.SDL_Delay(16);
        }

        SDL.SDL_DestroyTexture(boatTexture);
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
            // Рисуем горизонтальные линии для заполнения круга
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

    static void DrawText(IntPtr renderer, IntPtr font, string text, int x, int y, SDL.SDL_Color color)
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
        if (player.Level == 1)
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
                x = 2,
                y = 2,
                w = 12,
                h = 12
            };
            SDL.SDL_RenderFillRect(renderer, ref bodyRect);

            SDL.SDL_SetRenderDrawColor(renderer, 255, 255, 0, 255);
            SDL.SDL_Rect noseRect = new SDL.SDL_Rect
            {
                x = 12,
                y = 6,
                w = 4,
                h = 4
            };
            SDL.SDL_RenderFillRect(renderer, ref noseRect);
            SDL.SDL_SetRenderTarget(renderer, IntPtr.Zero); 
            return boatTexture;   

        }
        else
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
    }
}

class Player
{
    public bool IsFishing { get; set; } = false;
    public bool IsDead => CurrentHeath <= 0;
    public bool FishIsBiting { get; set; } = false;
    public uint BiteTimestamp { get; set; } = 0;
    public List<Fish> Inventory { get; private set; } = new List<Fish>();
    public int MaxInventorySlots { get; set; } = 10; 
    public int Money { get; set; } = 0;
    public int Level { get; set; } = 1;
    public float Experience { get; set; } = 0;
    public float X { get; set; }
    public float Y { get; set; }
    public float Speed { get; set; }
    public float MaxHealth { get; set; } = 100f;
    public float CurrentHeath { get; set; } = 100f;
    public float velocityX = 0f;
    public float velocityY = 0f;
    public float ClickStrength { get; set; } = 1.0f;
    public double Angle { get; set; } = 0;

    public Player()
    {
        X = 0f;
        Y = 0f;
        Speed = 10f;
    }
    public Player(float[,] map, int mapWidth, int mapHeight, int tileSize, float speed)
    {
        Spawn(map, mapWidth, mapHeight, tileSize);
        Speed = speed;
    }
    public static void Move(Player player, float[,] map, int mapWidth, int mapHeight, int tileSize)
    {   
        if (player.IsFishing)
            return;
        IntPtr keyState = SDL.SDL_GetKeyboardState(out _);
        float friction = 0.92f;

        unsafe
        {
            byte* keys = (byte*)keyState;

            if (keys[(int)SDL.SDL_Scancode.SDL_SCANCODE_W] != 0) player.velocityY -= player.Speed;
            if (keys[(int)SDL.SDL_Scancode.SDL_SCANCODE_S] != 0) player.velocityY += player.Speed;
            if (keys[(int)SDL.SDL_Scancode.SDL_SCANCODE_A] != 0) player.velocityX -= player.Speed;
            if (keys[(int)SDL.SDL_Scancode.SDL_SCANCODE_D] != 0) player.velocityX += player.Speed;
        }

        player.velocityX *= friction;
        player.velocityY *= friction;

        float moveX = player.velocityX;
        float moveY = player.velocityY;

        if (Math.Abs(player.velocityX) > 0.1 || Math.Abs(player.velocityY) > 0.1) 
        {
            player.Angle = Math.Atan2(player.velocityY, player.velocityX) * (180.0 / Math.PI);
        }

        float nextX = player.X + moveX;
        float nextY = player.Y + moveY;

        int mapX= (int)(nextX / tileSize);
        int mapY= (int)(nextY / tileSize);
        
        if (mapX >= 0 && mapX < mapWidth && mapY >= 0 && mapY < mapHeight)
        {   
            if (map[mapX, mapY] < 0.75f)
            {
                player.X = nextX;
                player.Y = nextY;
            }
            else
            {   
                player.velocityX = 0f;
                player.velocityY = 0f;
                Console.WriteLine("Sand! Can't swimm on sand.");
            }
        }
    }

    void Spawn(float [,] map, int mapWidth, int mapHeight, int tileSize)
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
                                X = x * tileSize + tileSize / 2;
                                Y = y * tileSize + tileSize / 2;
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
            X = (mapWidth * tileSize) / 2;
            Y = (mapHeight * tileSize) / 2;
            Console.WriteLine("Failed to find spawn point, defaulting to center.");
        }  
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
    public void TakeDamage(float amount)
    {
        CurrentHeath -= amount;
        CurrentHeath = (CurrentHeath < 0) ? 0 : CurrentHeath;
    }
}

public enum FishRarity
{
    Common,
    Rare,
    Epic,
    Legendary

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


    public Fish(string name, int requiredLevel, FishRarity rarity)
    {
        Name = name;
        RequiredLevel = requiredLevel;
        Rarity = rarity;
        GenerateStats();
    }

    public static Fish GenerateFishAt(float depth, float fishNoise)
    {
        FishRarity rarity;
        string name;

        // Логика редкости в зависимости от глубины (depth < 0.45f - глубоко)
        if (depth < 0.25f) { rarity = (fishNoise > 0.9f) ? FishRarity.Legendary : FishRarity.Epic; name = "Глубинный Ужас"; }
        else if (depth < 0.45f) { rarity = (fishNoise > 0.85f) ? FishRarity.Epic : FishRarity.Rare; name = "Синий Тунец"; }
        else { rarity = (fishNoise > 0.9f) ? FishRarity.Rare : FishRarity.Common; name = "Золотистый Карась"; }

        return new Fish(name, 1, rarity);
    }

    private void GenerateStats()
    {
        Random rand = new Random();
        RarityMultiplier = GetRarityMultiplier();
        Weight = (float)(rand.NextDouble() * 1.5 * RarityMultiplier); ;
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
    public void Draw(IntPtr renderer, float[,] map, float[,] fishMap, bool[,] discovered, Player player, Map miniMap, int screenWidth, int screenHeight, int mapWidth, int mapHeight)
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

        int startX = miniMap.isFullScreen ? 0 : (int)(player.X / 10) - 40;
        int endX = miniMap.isFullScreen ? mapWidth : (int)(player.X / 10) + 40;
        int startY = miniMap.isFullScreen ? 0 : (int)(player.Y / 10) - 40;
        int endY = miniMap.isFullScreen ? mapHeight : (int)(player.Y / 10) + 40;

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
        float pX_ratio = (float)(player.X / 10 - startX) / totalVisibleX;
        float pY_ratio = (float)(player.Y / 10 - startY) / totalVisibleY;

        SDL.SDL_SetRenderDrawColor(renderer, 255, 50, 50, 255);
        SDL.SDL_Rect playerMarker = new SDL.SDL_Rect {
            x = posX + (int)(pX_ratio * mapSizeX) - 3,
            y = posY + (int)(pY_ratio * mapSizeY) - 3,
            w = 6, h = 6
        };
        SDL.SDL_RenderFillRect(renderer, ref playerMarker);
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