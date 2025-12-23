// using SDL2;
// using System;
// using System.IO;

// internal class Program
// {   
//     static bool IsLand(float h) => h >= 0.82f;
//     static bool IsShallowWater(float h) => h >= 0.6f && h < 0.8f;
//     static bool IsDeepWater(float h) => h < 0.6f;
//     const int ScreenWidth = 800;
//     const int ScreenHeight = 600;
//     const int TileSize = 8;
//     const int MapWidth = (ScreenWidth * 4) / TileSize;
//     const int MapHeight = (ScreenHeight * 4) / TileSize;
//     const string SaveFileName = "big_map.dat";

//     static float PlayerX = 1500; 
//     static float PlayerY = 1500;

//     static float PierX = 1500;
//     static float PierY = 1500;    
    
//     static float CamX = 0;
//     static float CamY = 0;
    
//     static void Main()
//     {
//         SDL.SDL_Init(SDL.SDL_INIT_VIDEO);
//         IntPtr window = SDL.SDL_CreateWindow("GAZE INTO ABYSS", SDL.SDL_WINDOWPOS_CENTERED, SDL.SDL_WINDOWPOS_CENTERED, ScreenWidth, ScreenHeight, SDL.SDL_WindowFlags.SDL_WINDOW_SHOWN);
//         IntPtr renderer = SDL.SDL_CreateRenderer(window, -1, SDL.SDL_RendererFlags.SDL_RENDERER_ACCELERATED);

//         float[,] heightMap = new float[MapWidth, MapHeight];

//         if (File.Exists(SaveFileName)) { LoadMapData(heightMap); }
//         else { GenerateMap(heightMap); SaveMapData(heightMap); }

//         PlacePlayerAndPierOnShore(heightMap);

//         IntPtr worldTexture = BakeBigTexture(renderer, heightMap);

//         bool running = true;
//         while (running)
//         {   
//             while (SDL.SDL_PollEvent(out SDL.SDL_Event e) != 0)
//             {
//                 if (e.type == SDL.SDL_EventType.SDL_QUIT) running = false;
                
//                 if (e.type == SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN)
//                 {
//                     int mx, my;
//                     SDL.SDL_GetMouseState(out mx, out my);
//                     Console.WriteLine($"Клик в мире: {mx + CamX}, {my + CamY}");
//                 }
//             }

//             UpdateInput();
//             bool inPierZone = IsNearPier(PlayerX, PlayerY, PierX, PierY, 50f);
//             if (inPierZone)
//             {
//                 // Временно — просто текст в консоли или на экране
//                 Console.WriteLine("Нажми E, чтобы продать рыбу"); // пока в консоль

//                 // Проверка нажатия F
//                 IntPtr keystate = SDL.SDL_GetKeyboardState(out _);
//                 unsafe
//                 {
//                     byte* keys = (byte*)keystate;
//                     if (keys[(int)SDL.SDL_Scancode.SDL_SCANCODE_E] != 0)
//                     {
//                         Console.WriteLine("🐟 Рыба продана!");
//                     }
//                 }
//             }
//             CamX = PlayerX - ScreenWidth / 2;
//             CamY = PlayerY - ScreenHeight / 2;

//             // Ограничиваем камеру краями мира
//             CamX = Math.Clamp(CamX, 0, (MapWidth * TileSize) - ScreenWidth);
//             CamY = Math.Clamp(CamY, 0, (MapHeight * TileSize) - ScreenHeight);

//             SDL.SDL_RenderClear(renderer);

//             SDL.SDL_Rect srcRect = new SDL.SDL_Rect { x = (int)CamX, y = (int)CamY, w = ScreenWidth, h = ScreenHeight };
//             SDL.SDL_Rect destRect = new SDL.SDL_Rect { x = 0, y = 0, w = ScreenWidth, h = ScreenHeight };
//             SDL.SDL_RenderCopy(renderer, worldTexture, ref srcRect, ref destRect);

//             SDL.SDL_SetRenderDrawColor(renderer, 255, 0, 0, 255);
//             SDL.SDL_Rect playerShow = new SDL.SDL_Rect { 
//                 x = (int)(PlayerX - CamX) - 10, 
//                 y = (int)(PlayerY - CamY) - 10, 
//                 w = 20, 
//                 h = 20 
//             };
//             SDL.SDL_RenderFillRect(renderer, ref playerShow);

//             SDL.SDL_SetRenderDrawColor(renderer, 117, 0, 0, 255);
//             SDL.SDL_Rect pierShow = new SDL.SDL_Rect
//             {
//                 x = (int)(PierX - CamX) - 20,
//                 y = (int)(PierY - CamY) - 20,
//                 w = 40,
//                 h = 40
//             };
//             SDL.SDL_RenderDrawRect(renderer, ref pierShow);

//             SDL.SDL_SetRenderDrawColor(renderer, 255, 255, 0, 255);
//             SDL.SDL_Rect zoneShow = new SDL.SDL_Rect
//             {
//                 x = (int)(PierX - CamX) - 50,
//                 y = (int)(PierY - CamY) - 50,
//                 w = 100,
//                 h = 100
//             };
//             SDL.SDL_RenderDrawRect(renderer, ref zoneShow);

//             SDL.SDL_RenderPresent(renderer);
//             SDL.SDL_Delay(10);
//         }

//         SDL.SDL_DestroyTexture(worldTexture);
//         SDL.SDL_Quit();
//     }

//     static void UpdateInput()
//     {
//         IntPtr keystate = SDL.SDL_GetKeyboardState(out _);
//         unsafe {
//             byte* keys = (byte*)keystate;
//             if (keys[(int)SDL.SDL_Scancode.SDL_SCANCODE_W] != 0) PlayerY -= 1;
//             if (keys[(int)SDL.SDL_Scancode.SDL_SCANCODE_S] != 0) PlayerY += 1;
//             if (keys[(int)SDL.SDL_Scancode.SDL_SCANCODE_A] != 0) PlayerX -= 1;
//             if (keys[(int)SDL.SDL_Scancode.SDL_SCANCODE_D] != 0) PlayerX += 1;
//         }
//     }

//     // Находит берег и ставит игрока + пирс рядом
//     static void PlacePlayerAndPierOnShore(float[,] map)
//     {

//         // Простой поиск: ищем клетку суши с соседней мелководной
//         for (int x = 50; x < MapWidth; x++){
//             for (int y = 50; y < MapHeight; y++)
//                 if (map[x, y] > 0.82f) { 
//                     PlayerX = x * TileSize + 10; 
//                     PlayerY = y * TileSize + 10; 
//                     PierX = x * TileSize;
//                     PierY = y * TileSize;
//                     return; 
//                 }
//         }

//         // Если не нашли — ставим в угол (аварийный вариант)
//         PlayerX = 100 * TileSize;
//         PlayerY = 100 * TileSize;
//         PierX = PlayerX + TileSize * 2;
//         PierY = PlayerY;
//     }

//     static bool IsNearPier(float px, float py, float pierX, float pierY, float radius = 50f)
//     {
//         float dx = px - pierX;
//         float dy = py - pierY;
//         return dx * dx + dy * dy <= radius * radius;
//     }
//     static IntPtr BakeBigTexture(IntPtr renderer, float[,] map)
//     {
//         int texW = MapWidth * TileSize;
//         int texH = MapHeight * TileSize;
//         IntPtr tex = SDL.SDL_CreateTexture(renderer, SDL.SDL_PIXELFORMAT_RGBA8888, (int)SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_TARGET, texW, texH);
//         SDL.SDL_SetRenderTarget(renderer, tex);

//         for (int x = 0; x < MapWidth; x++)
//         {
//             for (int y = 0; y < MapHeight; y++)
//             {
//                 float h = map[x, y];
//                 if (h < 0.6f) SDL.SDL_SetRenderDrawColor(renderer, 0, 20, 70, 255);
//                 else if (h < 0.8f) SDL.SDL_SetRenderDrawColor(renderer, 0, 80, 160, 255);
//                 else if (h < 0.83f) SDL.SDL_SetRenderDrawColor(renderer, 210, 190, 130, 255);
//                 else SDL.SDL_SetRenderDrawColor(renderer, 30, 110, 30, 255);

//                 SDL.SDL_Rect r = new SDL.SDL_Rect { x = x * TileSize, y = y * TileSize, w = TileSize, h = TileSize };
//                 SDL.SDL_RenderFillRect(renderer, ref r);
//             }
//         }
//         SDL.SDL_SetRenderTarget(renderer, IntPtr.Zero);
//         return tex;
//     }

//     static void GenerateMap(float[,] map) {
//         var noise = new FastNoiseLite();
//         noise.SetFrequency(0.01f);
//         for (int x = 0; x < MapWidth; x++)
//             for (int y = 0; y < MapHeight; y++)
//                 map[x,y] = (noise.GetNoise(x, y) + 1f) / 2f;
//     }

//     static void SaveMapData(float[,] map) {
//         using var w = new BinaryWriter(File.Open(SaveFileName, FileMode.Create));
//         for (int x = 0; x < MapWidth; x++) for (int y = 0; y < MapHeight; y++) w.Write(map[x,y]);
//     }

//     static void LoadMapData(float[,] map) {
//         using var r = new BinaryReader(File.Open(SaveFileName, FileMode.Open));
//         for (int x = 0; x < MapWidth; x++) for (int y = 0; y < MapHeight; y++) map[x,y] = r.ReadSingle();
//     }
// }