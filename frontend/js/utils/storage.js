// VI: Wrapper xử lý LocalStorage, tập trung logic lưu trữ (SRP) để tránh lặp code. 
// EN: LocalStorage wrapper, centralizing storage logic (SRP) to prevent code duplication.

export function getStorageData(key, defaultValue = []) {
    // VI: Fallback về default nếu chưa có dữ liệu hoặc JSON bị hỏng.
    // EN: Fall back to the default when nothing is stored or the JSON is corrupted.
    try {
        const dataJSON = localStorage.getItem(key);
        return dataJSON ? JSON.parse(dataJSON) : defaultValue;
    } catch (e) {
        return defaultValue;
    }
}

export function setStorageData(key, data) {
    localStorage.setItem(key, JSON.stringify(data));
}
