using UnityEngine;
using XNClient.ChessBoard;

public class MainMenuBoardBackdrop : MonoBehaviour
{
    private const int BoardSize = 19;
    private const float LandscapeOrthographicSize = 15f;
    private const float PortraitOrthographicSize = 64f / 3f;
    private const float CameraHeight = 30f;

    private static readonly (int x, int z, bool black)[] LandscapeStones =
    {
        (2, 2, false), (5, 2, true), (2, 3, false), (3, 3, true),
        (4, 4, false), (2, 5, true), (3, 5, true), (5, 5, false), (4, 6, true),
    };

    private static readonly (int x, int z, bool black)[] PortraitStones =
    {
        (2, 2, false), (3, 2, true), (4, 3, false), (3, 4, true),
        (5, 4, false), (4, 5, true), (2, 6, false), (6, 6, true),
    };

    public RectGrid board;
    public GameObject blackStonePrefab;
    public GameObject whiteStonePrefab;

    private Camera boardCamera;
    private GameObject landscapeStones;
    private GameObject portraitStones;
    private int lastScreenWidth;
    private int lastScreenHeight;

    private void Start()
    {
        Build();
    }

    public void Build()
    {
        if (landscapeStones != null) {
            return;
        }

        boardCamera = GetComponent<Camera>();
        if (boardCamera == null || board == null || blackStonePrefab == null || whiteStonePrefab == null) {
            Debug.LogError("Main menu board backdrop is missing a camera, board or stone prefab.", this);
            return;
        }

        board.InitGrid(BoardSize);
        landscapeStones = CreateStones("LandscapeStones", LandscapeStones);
        portraitStones = CreateStones("PortraitStones", PortraitStones);
        ApplyFrame(Screen.width, Screen.height);
    }

    private void Update()
    {
        if (boardCamera != null && (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)) {
            ApplyFrame(Screen.width, Screen.height);
        }
    }

    private GameObject CreateStones(string rootName, (int x, int z, bool black)[] stones)
    {
        GameObject root = new GameObject(rootName);
        root.transform.SetParent(board.transform, false);
        foreach (var stone in stones) {
            GameObject prefab = stone.black ? blackStonePrefab : whiteStonePrefab;
            GameObject instance = Instantiate(prefab, root.transform);
            instance.transform.localPosition = board.GetCellCenterLocalPosition(stone.x, stone.z);
        }
        return root;
    }

    public void ApplyFrame(int width, int height)
    {
        if (boardCamera == null || board == null || width <= 0 || height <= 0) {
            return;
        }

        bool portrait = height > width;
        boardCamera.orthographic = true;
        boardCamera.orthographicSize = portrait ? PortraitOrthographicSize : LandscapeOrthographicSize;
        boardCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        Vector3 anchor = portrait
            ? board.GetCellCenterLocalPosition(3, 3)
            : board.GetCellCenterLocalPosition(0, 1);
        anchor = board.transform.TransformPoint(anchor);
        float aspect = (float)width / height;
        float targetX = portrait ? 143f / 720f : 756f / 1600f;
        float targetY = portrait ? 1f - 118f / 1280f : 1f - 45f / 900f;
        float cameraX = anchor.x - (targetX - 0.5f) * 2f * boardCamera.orthographicSize * aspect;
        float cameraZ = anchor.z - (targetY - 0.5f) * 2f * boardCamera.orthographicSize;
        boardCamera.transform.position = new Vector3(cameraX, anchor.y + CameraHeight, cameraZ);

        if (landscapeStones != null && portraitStones != null) {
            landscapeStones.SetActive(!portrait);
            portraitStones.SetActive(portrait);
        }
        lastScreenWidth = width;
        lastScreenHeight = height;
    }
}
