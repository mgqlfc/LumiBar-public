# 本曲歌词偏移与默认字体

v34 起，歌词同步偏移按 `media.key` 独立保存，重启后保留；未设置的歌曲从 0 秒开始。旧版全局 `LyricsOffset` 不再应用，设置页面已删除全局微调。

`state.get.lyrics.offsetSeconds` 和 `settings.lyricsOffset` 都表示当前歌曲的偏移；`settings.lyricsOffsetTrackKey` 给出对应的歌曲标识。无歌曲时偏移为 0。

调整示例：

```json
{"method":"settings.patch","params":{"lyricsOffset":1.5,"lyricsOffsetTrackKey":"从 media.key 读取的值"}}
```

偏移支持 -60 至 +60 秒，正数让歌词提前。0 会清除本曲记录。后端在执行期间检查歌曲是否切换；标识过期返回 `stale_track`，无歌曲返回 `no_session`。兼容旧客户端不传标识的调用，但建议新版始终携带标识。后台保存的其他歌曲偏移不会下发给前端。

字体：`chineseFontFamily` 与 `westernFontFamily` 为 null 时，现在分别使用内置苹方与 Geist。状态同时提供 `defaultChineseFontFamily: "PingFang SC"` 和 `defaultWesternFontFamily: "Geist"`。内置 WinUI 3 通过应用资源 URI 加载字体，不要求系统安装；自定义选择电脑字体仍保留。

备选前端若要在朋友电脑上使用同样字体，也需要随自己的前端包携带字体资源并按各自框架加载。内置软件的文件位于交付目录 `Assets/Fonts`，Geist 附带 OFL 文本，字体文件中的原始元数据保持不变。
