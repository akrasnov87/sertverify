using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SertCheck.Models
{
    [Table("dd_files", Schema = "core")]
    public class File
    {
        [Key]
        public Guid id { get; set; }

        public byte[] ba_data { get; set; }
        public string c_type { get; set; }

        public Guid f_document { get; set; }
        public DateTime dx_created { get; set; }
        public bool b_verify { get; set; }
        public string c_gosuslugi_key { get; set; }
        public bool sn_delete { get; set; }
        public DateTime? d_date { get; set; }
        public string c_notice { get; set; }
    }
}
