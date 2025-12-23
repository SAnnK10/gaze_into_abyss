using SDL2;
using System;
using System.Data;
using System.IO;

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
        
        IntPtr fogTexture = GenerateFog(renderer, MAP_WIDTH, MAP_HEIGHT, TILE_SIZE);

        IntPtr boatTexture = GenerateBoat(renderer);

        Player player = new Player(map, MAP_WIDTH, MAP_HEIGHT, TILE_SIZE, 0.1f);

        while (running)
        {
            while (SDL.SDL_PollEvent(out e) != 0)
            {
                if (e.type == SDL.SDL_EventType.SDL_QUIT)
                    running = false;
            }   

            uint ticks = SDL.SDL_GetTicks();
            float wave = (float)Math.Sin(ticks * 0.0001f);
            byte colorMod = (byte)(215 + (wave * 40));
            SDL.SDL_SetTextureColorMod(worldTexture, colorMod, colorMod, 255);

            Player.Move(player, map, MAP_WIDTH, MAP_HEIGHT, TILE_SIZE);

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
}