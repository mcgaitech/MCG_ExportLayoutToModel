using System.Collections.Generic;
using MCGCadPlugin.Models.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.Data
{
    /// <summary>
    /// Interface cho data access layer — SQLite CRUD operations.
    /// </summary>
    public interface IDetailDesignRepository
    {
        // Schema
        /// <summary>Khởi tạo schema + seed profiles</summary>
        void InitializeDatabase();

        // Panels
        /// <summary>
        /// Thêm hoặc cập nhật panel. Duplicate detection qua root_block_handle (primary)
        /// hoặc (name+side+drawing_filepath) (fallback). Trả về guid final (cũ nếu existing,
        /// mới nếu insert) + mutate panel.Guid in-place.
        /// </summary>
        string UpsertPanel(PanelContext panel);

        /// <summary>Lấy panel theo GUID</summary>
        PanelContext GetPanel(string guid);

        // Elements
        /// <summary>Thêm hoặc cập nhật structural element</summary>
        void UpsertElement(StructuralElementModel elem);

        /// <summary>Lấy element theo GUID</summary>
        StructuralElementModel GetElement(string guid);

        /// <summary>Lấy tất cả elements của 1 panel</summary>
        List<StructuralElementModel> GetElementsByPanel(string panelGuid);

        // Profiles
        /// <summary>Lấy tất cả profiles từ catalog</summary>
        List<ProfileModel> GetAllProfiles();

        /// <summary>Lấy profile theo code (HP120x7)</summary>
        ProfileModel GetProfileByCode(string code);
    }
}
