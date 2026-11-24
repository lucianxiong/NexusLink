# 自定义 sing-box rule-set

「自定义 sing-box rule-set」位于应用内的「路由规则设置」窗口，允许你用自己的规则集定义，替换或补充应用内置的规则集来源。

> 注意：当前发布版本的界面**没有打开路由规则设置窗口的入口**（代码保留）。本文说明的是代码的实际行为，供源码使用者与后续开放该入口时参考。

在「路由规则设置」窗口中，为某条路由规则选择一个 JSON 文件（建议用「浏览」按钮选绝对路径）。文件内容是一个对象数组，每个对象的字段与 sing-box 的 `route.rule_set` 条目一致：

| 字段 | 说明 |
| --- | --- |
| `tag` | 规则集标签。路由规则里引用的 rule_set 名称与之相同时，优先使用你的定义 |
| `type` | `local`（本地文件）或 `remote`（远程下载） |
| `format` | `binary`（`.srs` 二进制）或 `source`（`.json` 源格式） |
| `path` | `type` 为 `local` 时的文件路径 |
| `url` | `type` 为 `remote` 时的下载地址 |
| `http_client` | 远程下载所用的 HTTP 客户端标签（可选） |
| `update_interval` | 远程规则集的更新间隔（可选） |

其中 `tag`、`type`、`format` 三项必填，缺少的条目会被忽略。

## 生效顺序

应用生成 sing-box 配置时，对每个被引用的 rule_set 标签依次查找：

1. 你的自定义文件里有同名定义 → 使用你的定义；
2. 否则，程序目录 `bin\srss\` 下有同名的 `.srs` 文件 → 作为本地规则集使用；
3. 否则，从内置的远程规则集源下载。

## 示例

把一个本地规则集和一个远程规则集交给应用使用：

```json
[
  {
    "tag": "geosite-my-list",
    "type": "local",
    "format": "source",
    "path": "D:\\NexusLink\\rules\\my-list.json"
  },
  {
    "tag": "geoip-my-list",
    "type": "remote",
    "format": "binary",
    "url": "https://example.com/rules/geoip-my-list.srs",
    "update_interval": "24h"
  }
]
```

规则集文件的格式遵循 [sing-box rule-set 文档](https://sing-box.sagernet.org/zh/configuration/rule-set/)。
