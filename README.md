# Piston Csharp

繁體中文說明文件。

專案網址: https://github.com/MKMonkeyCat/piston-csharp.git
（複製並以 `git clone` 取得原始碼）

## 簡介

這是 piston-csharp 倉庫，包含範例應用 `MyApp`、核心函式庫 `Piston.Core` 及其單元測試專案 `Piston.Core.Tests`。專案示範了一個用於程式碼執行/包裝的客戶端庫與相關測試資料。

## 倉庫主要結構

- `Piston.Core/` — 核心函式庫實作
- `Piston.Core.Tests/` — 單元測試
- `LICENSE` — 授權說明

## 自動補全支援以下語言：

- C
- C++
- Java
- C#

Python, JavaScript 等語言無須前置包裝，直接傳送原始程式碼即可。
若有其他語言需求，歡迎提出 issue 或 pull request。

允許使用 `ON` 或 `OFF` 來控制是否啟用包裝功能，具體使用方式如下：
註解 > 函數參數 > 預設值(true)

```
// PISTON-WRAP: ON
```

```
/* PISTON-WRAP: ON */
```

```
# PISTON-WRAP: ON
```

<details>
<summary>（註：以下為示範程式碼片段，實際使用請參考專案原始碼或查閱 Piston.Core.Tests/TestData）</summary>

### C

#### IN

```c
printf("Hello, World!\n");
```

#### OUT

```c
#include <stdio.h>
int main() {
printf("Hello, World!\n");
return 0;
}
```

### C++

#### IN

```cpp
cout << "Hello, World!" << endl;
```

#### OUT

```cpp
#include <iostream>
using namespace std;
int main() {
cout << "Hello, World!" << endl;
return 0;
}
```

### Java

#### IN

```java
System.out.println("Hello, World!");
```

#### OUT

```java
public class Main {
public static void main(String[] args) {
System.out.println("Hello, World!");
}
}
```

</details>

## 先決條件

- `Piston.Core` 需要 .NET Standard 2.0 或更高版本 (為了兼容性，害我寫的好痛苦...)
- `Piston.Core.Tests` 需要 .NET 10.0 SDK 或更高版本

## 建置與測試

在倉庫根目錄執行：

```bash
dotnet build Piston.Csharp.slnx
dotnet test
```

單獨執行測試專案：

```bash
dotnet test ./Piston.Core.Tests/Piston.Core.Tests.csproj
```

## 貢獻

歡迎開 issue 或提出 pull request。請在提交前確保相關單元測試通過。

## 授權

請參閱倉庫根目錄的 LICENSE 檔案。
