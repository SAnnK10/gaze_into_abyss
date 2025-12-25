using SDL2;
using System;
using System.Data;
using System.IO;
using System.Collections.Generic;

internal class Program
{   
    const int SCREEN_WIDTH = 800;
    const int SCREEN_HEIGHT = 600;
    const int TILE_SIZE = 10;
    const int MAP_WIDTH = SCREEN_WIDTH * 5 / TILE_SIZE;
    const int MAP_HEIGHT = SCREEN_HEIGHT * 5 / TILE_SIZE;
    static float camX, camY = 0f;

    static void Main()
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

        if (SDL.SDL_Init(SDL.SDL_INIT_VIDEO) != 0)
        {
            return;
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
        IntPtr fishTexture = BakeFishTexture(
            renderer, 
            fishMap,
            map, 
            MAP_WIDTH, 
            MAP_HEIGHT, 
            TILE_SIZE
        );

        IntPtr fogTexture = GenerateFog(renderer, MAP_WIDTH, MAP_HEIGHT, TILE_SIZE);

        IntPtr boatTexture = GenerateBoat(renderer);

        Player player = new Player(map, MAP_WIDTH, MAP_HEIGHT, TILE_SIZE, 0.5f);
        

        while (running)
        {
            while (SDL.SDL_PollEvent(out e) != 0)
            {
                if (e.type == SDL.SDL_EventType.SDL_QUIT) running = false;

                if (e.type == SDL.SDL_EventType.SDL_KEYDOWN)
                {
                    int pX = (int)(player.X / TILE_SIZE);
                    int pY = (int)(player.Y / TILE_SIZE);

                    if (e.key.keysym.scancode == SDL.SDL_Scancode.SDL_SCANCODE_E)
                    {
                        if (!player.IsFishing && fishMap[pX, pY] > 0.7f)
                        {
                            player.IsFishing = true;
                            player.FishIsBiting = false;
                            player.velocityX = 0f;
                            player.velocityY = 0f;
                            player.BiteTimestamp = SDL.SDL_GetTicks() + (uint)new Random().Next(2000, 4000);
                            Console.WriteLine("Удочка заброшена...");
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
                    if (e.key.keysym.scancode == SDL.SDL_Scancode.SDL_SCANCODE_SPACE && fishingGame.IsActive) fishingGame.PlayerClick(); 
                }
            }   

            uint ticks = SDL.SDL_GetTicks();

            Player.Move(player, map, MAP_WIDTH, MAP_HEIGHT, TILE_SIZE);

            if (player.IsFishing && !player.FishIsBiting)
            {
                if (ticks >= player.BiteTimestamp)
                {
                    player.FishIsBiting = true;
                    Console.WriteLine("Рыба клюнула! Нажмите пробел, чтобы поймать её!");
                }
            }

            if (fishingGame.IsActive)
            {
                fishingGame.Update();

                if (fishingGame.CheckWin())
                {
                    if (player.AddToInventory(currentFish))
                    {
                        Console.WriteLine($"Вы добавили {currentFish.Name} в инвентарь.");
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
            }


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
            
            SDL.SDL_RenderCopy(renderer, fishTexture, ref srcRect, ref destRect);

            SDL.SDL_SetRenderTarget(renderer, fogTexture);
            SDL.SDL_SetRenderDrawBlendMode(renderer, SDL.SDL_BlendMode.SDL_BLENDMODE_NONE);
            SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 0);
            SDL.SDL_Rect revealRect = new SDL.SDL_Rect
            {
                x = (int)(player.X - 200),
                y = (int)(player.Y - 200),
                w = 400,
                h = 400
            };
            SDL.SDL_RenderFillRect(renderer, ref revealRect);
            SDL.SDL_SetRenderTarget(renderer, IntPtr.Zero);
            SDL.SDL_RenderCopy(renderer, fogTexture, ref srcRect, ref destRect);

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

            if (player.FishIsBiting && !fishingGame.IsActive)
            {
                SDL.SDL_SetRenderDrawColor(renderer, 255, 255, 255, 255);
                SDL.SDL_Rect alertRect = new SDL.SDL_Rect {
                    x = (int)(player.X - camX + 4),
                    y = (int)(player.Y - camY - 25), 
                    w = 6, h = 12
                };
                SDL.SDL_RenderFillRect(renderer, ref alertRect);
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

            SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 150);
            SDL.SDL_Rect uiRect = new SDL.SDL_Rect { x = 10, y = 10, w = 180, h = 80 };
            SDL.SDL_RenderFillRect(renderer, ref uiRect);

            SDL.SDL_SetRenderDrawColor(renderer, 200, 200, 0, 255);
            float cargoFill = (float)player.Inventory.Count / player.MaxInventorySlots;
            SDL.SDL_Rect cargoBar = new SDL.SDL_Rect { x = 20, y = 50, w = (int)(150 * cargoFill), h = 10 };
            SDL.SDL_RenderFillRect(renderer, ref cargoBar);

            SDL.SDL_RenderPresent(renderer);
            SDL.SDL_Delay(16);
        }

        SDL.SDL_DestroyTexture(boatTexture);
        SDL.SDL_DestroyTexture(fogTexture);
        SDL.SDL_DestroyTexture(worldTexture);
        SDL.SDL_DestroyRenderer(renderer);
        SDL.SDL_DestroyWindow(window);
        SDL.SDL_Quit();
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

    static IntPtr BakeFishTexture(IntPtr renderer, float[,] fishMap, float[,] map, int mapWidth, int mapHeight, int tileSize)
    {
        IntPtr texture = SDL.SDL_CreateTexture(renderer, SDL.SDL_PIXELFORMAT_RGBA8888, 
            (int)SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_TARGET, mapWidth * tileSize, mapHeight * tileSize);
        
        SDL.SDL_SetTextureBlendMode(texture, SDL.SDL_BlendMode.SDL_BLENDMODE_BLEND);
        SDL.SDL_SetRenderTarget(renderer, texture);
        SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 0); 
        SDL.SDL_RenderClear(renderer);

        Random rand = new Random();

        for (int x = 1; x < mapWidth - 1; x++)
        {
            for (int y = 1; y < mapHeight - 1; y++)
            {
                if (fishMap[x, y] > 0.7f && map[x, y] < 0.75f && x % 2 == 0 && y % 2 == 0)
                {
                    SDL.SDL_SetRenderDrawColor(renderer, 255, 255, 255, 180);
                    
                    for (int i = 0; i < 3; i++)
                    {
                        int px = x * tileSize + rand.Next(2, 8);
                        int py = y * tileSize + rand.Next(2, 8);
                        SDL.SDL_Rect bubble = new SDL.SDL_Rect { x = px, y = py, w = 2, h = 2 };
                        SDL.SDL_RenderFillRect(renderer, ref bubble);
                    }
                }
            }
        }

        SDL.SDL_SetRenderTarget(renderer, IntPtr.Zero);
        return texture;
    }

    static IntPtr GenerateFog(IntPtr renderer, int width, int height, int tileSize)
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
        SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 235);
        SDL.SDL_RenderClear(renderer);
        SDL.SDL_SetRenderTarget(renderer, IntPtr.Zero);
        return fogTexture;
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

    static IntPtr GenerateBoat(IntPtr renderer)
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
}

class Player
{
    public bool IsFishing { get; set; } = false;
    public bool FishIsBiting { get; set; } = false;
    public uint BiteTimestamp { get; set; } = 0;
    public List<Fish> Inventory { get; private set; } = new List<Fish>();
    public int MaxInventorySlots { get; set; } = 10; 
    public int Money { get; set; } = 0;
    public int Level { get; set; } = 1;
    public int Experience { get; set; } = 0;
    public float X { get; set; }
    public float Y { get; set; }
    public float Speed { get; set; }
    public float velocityX = 0f;
    public float velocityY = 0f;
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
        Random rand = new Random();
        bool found = false;
        int attempts = 0;
        int maxAttempts = 1000;
        int SearchRadius = 30 / tileSize;

        while (!found && attempts < maxAttempts)
        {
            attempts++;
            int x = rand.Next(mapWidth);
            int y = rand.Next(mapHeight);

            if (0.65f <= map[x,y] && map[x, y] < 0.75f)
            {   
                bool nearLand = false;
                for (int dx = -SearchRadius; dx <= SearchRadius; dx++)
                {
                    for (int dy = -SearchRadius; dy <= SearchRadius; dy++)
                    {
                        float neighborValue = map[x + dx, y + dy];

                        if (neighborValue >= 0.75f && neighborValue < 0.77f)
                        {
                            nearLand = true;
                            break;
                        }
                    }
                    if (nearLand) break;
                }

                if (!nearLand)
                {
                    continue;
                } 
                else
                {
                    X = x * tileSize + tileSize / 2;
                    Y = y * tileSize + tileSize / 2;
                    found = true;
                }
            }
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
            Inventory.Add(fish);
            Experience += (int)(fish.Weight * 10);
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
    public int RequiredLevel { get; set; }
    public FishRarity Rarity { get; set; }

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
        float rarityMultiplier = GetRarityMultiplier();
        Weight = (float)(rand.NextDouble() * 1.5 * rarityMultiplier); ;
        Price = (int)(Weight * 20 * rarityMultiplier);
    }

    private float GetRarityMultiplier()
    {
        return Rarity switch
        {
            FishRarity.Common => 1.0f,
            FishRarity.Rare => 2.5f,
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

        Tension -= 0.2f * fishPower;
    }

    public void PlayerClick()
    {
        Tension += 1.2f; 
        Progress += 10f;
    }

    public bool CheckWin() => Progress >= MaxProgress;
    public bool CheckLoss() => Math.Abs(Tension) >= 10f;
}