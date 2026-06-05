using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using XNClient.Logger;

public sealed class RemoteImageView
{
    private readonly MonoBehaviour coroutineOwner;
    private readonly Image image;
    private readonly Color emptyColor;
    private Coroutine downloadCoroutine;
    private Sprite sprite;
    private string spriteUrl;

    public RemoteImageView(MonoBehaviour coroutineOwner, Image image, Color emptyColor)
    {
        this.coroutineOwner = coroutineOwner;
        this.image = image;
        this.emptyColor = emptyColor;
        Clear();
    }

    public void Load(string imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl)) {
            Clear();
            return;
        }

        string safeUrl = imageUrl.Trim();
        if (safeUrl == spriteUrl && sprite != null) {
            return;
        }

        StopDownload();
        ClearSprite();
        ApplyEmptyImage();
        spriteUrl = safeUrl;

        if (coroutineOwner != null && coroutineOwner.gameObject.activeInHierarchy) {
            downloadCoroutine = coroutineOwner.StartCoroutine(DownloadImage(safeUrl));
        }
    }

    public void Clear()
    {
        StopDownload();
        spriteUrl = string.Empty;
        ClearSprite();
        ApplyEmptyImage();
    }

    private IEnumerator DownloadImage(string imageUrl)
    {
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(imageUrl)) {
            yield return request.SendWebRequest();

            downloadCoroutine = null;
            if (request.result != UnityWebRequest.Result.Success) {
                XNLogger.LogWarn("Download remote UI image failed.", ("url", imageUrl), ("err", request.error ?? string.Empty));
                yield break;
            }

            if (imageUrl != spriteUrl || image == null) {
                yield break;
            }

            Texture2D texture = DownloadHandlerTexture.GetContent(request);
            if (texture == null) {
                yield break;
            }

            ClearSprite();
            sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f));
            image.sprite = sprite;
            image.preserveAspect = true;
            image.color = Color.white;
        }
    }

    private void StopDownload()
    {
        if (downloadCoroutine != null && coroutineOwner != null) {
            coroutineOwner.StopCoroutine(downloadCoroutine);
            downloadCoroutine = null;
        }
    }

    private void ApplyEmptyImage()
    {
        if (image != null) {
            image.sprite = null;
            image.color = emptyColor;
        }
    }

    private void ClearSprite()
    {
        if (sprite == null) {
            return;
        }

        if (sprite.texture != null) {
            Object.Destroy(sprite.texture);
        }
        Object.Destroy(sprite);
        sprite = null;
    }
}
