# 🗂️ MinioFileManager – .NET 9 REST API for MinIO Object Storage

**MinioFileManager** is a production-ready REST API built with **.NET 9** that provides an easy and powerful interface for interacting with **MinIO**, an open-source, self-hosted, AWS S3–compatible object storage service.

---

## 🚀 Features

* ✅ Upload, download, and list files
* ✅ Copy and move files between buckets
* ✅ Delete single or multiple objects
* ✅ Manage object tags (metadata)
* ✅ Generate presigned URLs for direct upload/download
* ✅ Retrieve object info and tags
* ✅ Uses MinIO SDK v6.0.5+
* ✅ Built with .NET 9 and C# 12

---

## 🏗️ Tech Stack

* **.NET 9 Web API**
* **MinIO .NET SDK (v6.0.5+)**
* **Swagger / OpenAPI**
* **C# 12** with top-level statements and dependency injection

---

## 📦 Required NuGet Packages

```bash
dotnet add package Minio --version 6.0.5
dotnet add package Microsoft.Extensions.Options
dotnet add package Microsoft.AspNetCore.OpenApi
dotnet add package Swashbuckle.AspNetCore
```

---

## ⚙️ Project Structure

```
MinioSetup/
│
├── Controller/
│   └── MinioController.cs
│
├── Model/
│   └── MinioSettings.cs
│
├── Program.cs
├── appsettings.json
└── README.md
```

---

## ⚡ Quick Start

### 🧩 Prerequisite: MinIO Server

You’ll need a running MinIO instance.
For setup instructions, refer to:
🔗 **[Local-MinIO-Setup-for-Development](https://github.com/RealMadCrazy/Local-MinIO-Setup-for-Development)**

---

### 🧠 Configure MinIO Connection

Edit `appsettings.json`:

```json
{
  "Minio": {
    "Endpoint": "localhost:9000",
    "AccessKey": "minioadmin",
    "SecretKey": "minioadmin",
    "BucketName": "mybucket",
    "UseSSL": false
  }
}
```

---

### ▶️ Run the API

```bash
dotnet run
```

Open Swagger:
👉 [https://localhost:5001/swagger](https://localhost:5001/swagger)

---

## 🧩 API Endpoints Overview

| Endpoint                                | Method     | Description                         |
| --------------------------------------- | ---------- | ----------------------------------- |
| `/api/minio/upload`                     | **POST**   | Upload file to MinIO                |
| `/api/minio/download/{fileName}`        | **GET**    | Download file                       |
| `/api/minio/list`                       | **GET**    | List all buckets and objects        |
| `/api/minio/delete/{fileName}`          | **DELETE** | Delete a single object              |
| `/api/minio/bulkdelete`                 | **DELETE** | Delete multiple objects             |
| `/api/minio/copy`                       | **POST**   | Copy or move object between buckets |
| `/api/minio/presignedurl/{fileName}`    | **GET**    | Generate presigned GET URL          |
| `/api/minio/presignedupload/{fileName}` | **GET**    | Generate presigned PUT URL          |
| `/api/minio/info/{fileName}`            | **GET**    | Get object metadata and tags        |
| `/api/minio/tags/{fileName}`            | **POST**   | Set or update object tags           |

---

## 🧪 API Examples (Input & Output)

### 🟩 1. Upload File

**Endpoint:**
`POST /api/minio/upload`

**Input:**

* Form-Data:

  * `file`: *(File)* — The file to upload
  * `bucketName`: *(string, optional)* — Target bucket name

**Output:**

```json
{
  "file": "myphoto.png",
  "bucket": "mybucket",
  "status": "uploaded"
}
```

---

### 🟦 2. Download File

**Endpoint:**
`GET /api/minio/download/{fileName}`

**Input:**

* Path: `fileName` — Name of the file to download
* Query (optional): `bucketName`

**Output:**

* Returns file binary stream for direct download.

---

### 🟨 3. List Buckets and Objects

**Endpoint:**
`GET /api/minio/list`

**Input:**

* Query (optional): `prefix` — Filter results by prefix

**Output:**

```json
[
  {
    "bucket": "mybucket",
    "created": "2025-10-13T15:22:00Z",
    "count": 2,
    "objects": ["myphoto.png", "notes.pdf"]
  }
]
```

---

### 🟥 4. Delete a Single File

**Endpoint:**
`DELETE /api/minio/delete/{fileName}`

**Input:**

* Path: `fileName` — Name of the file to delete
* Query (optional): `bucketName`

**Output:**

```json
{
  "file": "myphoto.png",
  "bucket": "mybucket",
  "status": "deleted"
}
```

---

### 🟪 5. Bulk Delete Files

**Endpoint:**
`DELETE /api/minio/bulkdelete`

**Input:**

* Query (optional): `bucketName`
* Body (JSON array):

```json
["myphoto.png", "notes.pdf"]
```

**Output:**

```json
{
  "bucket": "mybucket",
  "count": 2,
  "status": "deleted"
}
```

---

### 🟫 6. Copy or Move File

**Endpoint:**
`POST /api/minio/copy`

**Input:**

* Query parameters:

  * `source`: *(string)* — Source object name
  * `destination`: *(string)* — Destination object name
  * `sourceBucket`: *(string, optional)* — Source bucket
  * `destinationBucket`: *(string, optional)* — Destination bucket
  * `cut`: *(bool, optional)* — If true, deletes original file (acts as “move”)

**Output:**

```json
{
  "from": "mybucket/image1.png",
  "to": "mybucket/image2.png",
  "renamed": true,
  "status": "moved"
}
```

---

### 🟦 7. Generate Presigned GET URL (Download)

**Endpoint:**
`GET /api/minio/presignedurl/{fileName}`

**Input:**

* Path: `fileName` — File to generate link for
* Query (optional): `bucketName`

**Output:**

```json
{
  "file": "myphoto.png",
  "bucket": "mybucket",
  "url": "http://localhost:9000/mybucket/myphoto.png?X-Amz-Algorithm=..."
}
```

---

### 🟧 8. Generate Presigned PUT URL (Upload)

**Endpoint:**
`GET /api/minio/presignedupload/{fileName}`

**Input:**

* Path: `fileName` — File name to upload to MinIO
* Query (optional): `bucketName`

**Output:**

```json
{
  "bucket": "mybucket",
  "file": "myfile.zip",
  "expiresIn": "10 minutes",
  "url": "http://localhost:9000/mybucket/myfile.zip?X-Amz-Algorithm=..."
}
```

Use this URL to upload directly to MinIO:

```powershell
Invoke-WebRequest -Uri "<presigned-url>" -Method PUT -InFile "C:\path\to\myfile.zip" -ContentType "application/zip"
```

---

### 🟩 9. Get Object Info and Tags

**Endpoint:**
`GET /api/minio/info/{fileName}`

**Input:**

* Path: `fileName` — File to inspect
* Query (optional): `bucketName`

**Output:**

```json
{
  "file": "myfile.zip",
  "bucket": "mybucket",
  "size": 1048576,
  "contentType": "application/zip",
  "lastModified": "2025-10-14T07:30:00Z",
  "metadata": {},
  "tags": {
    "Category": "PC",
    "Part": "Motherboard"
  }
}
```

---

### 🟦 10. Set or Update Object Tags

**Endpoint:**
`POST /api/minio/tags/{fileName}`

**Input:**

* Path: `fileName` — File to tag
* Query (optional): `bucketName`
* Body (JSON):

```json
{
  "Category": "PC",
  "Part": "Motherboard",
  "Brand": "ASUS"
}
```

**Output:**

```json
{
  "file": "myfile.zip",
  "bucket": "mybucket",
  "tags": {
    "Category": "PC",
    "Part": "Motherboard",
    "Brand": "ASUS"
  },
  "status": "tags-updated"
}
```

---

## 🧰 Requirements

* **.NET 9 SDK**
* **MinIO Server v6.0.5+**
* **Windows / macOS / Linux**

---

## 🧑‍💻 Developer Notes

* Built using dependency injection (`IMinioClient`)
* All operations are async
* Auto-creates buckets when missing
* Handles exceptions gracefully
* Fully documented with Swagger/OpenAPI
* Easy to extend with additional features (e.g., database metadata, audit logs)

---

## 📜 License

Licensed under the **MIT License** — free to use, modify, and distribute.

---

## 💡 Author

**Mad_Crazy**
🖥️ [GitHub Profile](https://github.com/RealMadCrazy)
📦 Project: **MinioFileManager**
🔗 Built with ❤️ using **.NET 9** and **MinIO SDK**
